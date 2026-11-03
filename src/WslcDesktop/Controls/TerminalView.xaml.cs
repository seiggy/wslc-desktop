using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WslcDesktop.Services;

namespace WslcDesktop.Controls;

/// <summary>
/// A real terminal: xterm.js in WebView2 rendering a Windows pseudoconsole (ConPTY) session, so full-screen programs,
/// colours, resize and mouse work. <see cref="StartAsync"/> throws when the terminal can't initialise so the caller can fall back.
/// </summary>
public sealed partial class TerminalView : UserControl
{
    private const string VirtualHost = "terminal.wslc.invalid";

    private readonly object _outputGate = new();
    private readonly List<byte> _outputBuffer = [];
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private ConPtySession? _session;
    private bool _initialised;
    private bool _flushQueued;
    private short _columns = TerminalProtocol.DefaultColumns;
    private short _rows = TerminalProtocol.DefaultRows;
    private string _fontFamily = TerminalProtocol.Fonts[0];
    private int _fontSize = 14;

    public TerminalView()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => ApplyStyle();
    }

    public bool IsRunning => _session is not null;

    private bool IsDark => ActualTheme != ElementTheme.Light;

    public async Task StartAsync(string session, string container, string shell, Action<int> onExit)
    {
        Stop();
        await EnsureInitialisedAsync();
        Web.CoreWebView2.PostWebMessageAsString(TerminalProtocol.CommandMessage("clear"));

        var commandLine = TerminalProtocol.BuildCommandLine("wslc", TerminalProtocol.ExecShellArguments(session, container, shell));
        ConPtySession? started = null;
        started = ConPtySession.Start(commandLine, _columns, _rows, QueueOutput, code => _dispatcher.TryEnqueue(() =>
        {
            if (ReferenceEquals(_session, started))
            {
                _session = null;
                started?.Dispose();
                onExit(code);
            }
        }));
        _session = started;

        try
        {
            ChildProcessTracker.Track(Process.GetProcessById(started.ProcessId));
        }
        catch (ArgumentException)
        {
            // Already gone; the exit callback reports it.
        }

        Focus(FocusState.Programmatic);
        Web.CoreWebView2.PostWebMessageAsString(TerminalProtocol.CommandMessage("focus"));
    }

    public void Stop()
    {
        var session = _session;
        _session = null;
        session?.Dispose();
    }

    public void Notice(string text)
    {
        if (_initialised)
        {
            Web.CoreWebView2.PostWebMessageAsString(TerminalProtocol.NoticeMessage(text));
        }
    }

    public void FocusTerminal()
    {
        if (_initialised)
        {
            Web.Focus(FocusState.Programmatic);
            Web.CoreWebView2.PostWebMessageAsString(TerminalProtocol.CommandMessage("focus"));
        }
    }

    public void SetFont(string family, int size)
    {
        (_fontFamily, _fontSize) = (family, size);
        ApplyStyle();
    }

    private async Task EnsureInitialisedAsync()
    {
        if (_initialised)
        {
            return;
        }

        var (_, background) = PaletteColors();
        Web.DefaultBackgroundColor = background;
        await Web.EnsureCoreWebView2Async();
        var core = Web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = false;
        core.SetVirtualHostNameToFolderMapping(VirtualHost, Path.Combine(AppContext.BaseDirectory, "Terminal", "web"), Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessage;

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFirstReady(Microsoft.Web.WebView2.Core.CoreWebView2 _, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (TerminalProtocol.ParseMessage(e.TryGetWebMessageAsString()) is { Kind: TerminalMessageKind.Ready } m)
            {
                (_columns, _rows) = (m.Columns, m.Rows);
                ready.TrySetResult();
            }
        }

        core.WebMessageReceived += OnFirstReady;
        core.Navigate($"https://{VirtualHost}/index.html");
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            core.WebMessageReceived -= OnFirstReady;
        }

        _initialised = true;
        ApplyStyle();
        Web.Opacity = 1;
    }

    private void OnWebMessage(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = TerminalProtocol.ParseMessage(e.TryGetWebMessageAsString());
        switch (message)
        {
            case { Kind: TerminalMessageKind.Input, Data: { } text }:
                _session?.Write(TerminalProtocol.EncodeInput(text));
                break;
            case { Kind: TerminalMessageKind.Binary, Data: { } bin }:
                _session?.Write(TerminalProtocol.EncodeBinaryInput(bin));
                break;
            case { Kind: TerminalMessageKind.Size or TerminalMessageKind.Ready } size when size.Columns > 0 && size.Rows > 0:
                (_columns, _rows) = (size.Columns, size.Rows);
                _session?.Resize(size.Columns, size.Rows);
                break;
        }
    }

    private (Windows.UI.Color Foreground, Windows.UI.Color Background) PaletteColors()
    {
        var palette = TerminalProtocol.Palette(IsDark);
        return (ParseColor(palette["foreground"]), ParseColor(palette["background"]));
    }

    private static Windows.UI.Color ParseColor(string hex) =>
        Windows.UI.Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));

    private void ApplyStyle()
    {
        var (_, background) = PaletteColors();
        Web.DefaultBackgroundColor = background;
        if (_initialised)
        {
            Web.CoreWebView2.PostWebMessageAsString(TerminalProtocol.StyleMessage(IsDark, _fontFamily, _fontSize));
        }
    }

    private void QueueOutput(byte[] buffer, int count)
    {
        lock (_outputGate)
        {
            for (var i = 0; i < count; i++)
            {
                _outputBuffer.Add(buffer[i]);
            }

            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
        }

        _dispatcher.TryEnqueue(FlushOutput);
    }

    private void FlushOutput()
    {
        byte[] data;
        lock (_outputGate)
        {
            data = [.. _outputBuffer];
            _outputBuffer.Clear();
            _flushQueued = false;
        }

        if (data.Length > 0 && _initialised)
        {
            Web.CoreWebView2.PostWebMessageAsString(TerminalProtocol.OutputMessage(data));
        }
    }
}
