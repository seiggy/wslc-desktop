using System.Collections.Concurrent;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WslcDesktop.Controls;

/// <summary>
/// Scrolling monospace text for logs and shell output. Lines may be appended from any thread;
/// they're batched onto the UI thread and the buffer is capped so a chatty container can't exhaust memory.
/// </summary>
public sealed partial class OutputView : UserControl
{
    private const int MaxLines = 2000;

    private readonly ConcurrentQueue<string> _pending = new();
    private readonly LinkedList<string> _lines = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public OutputView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Flush();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public bool AutoScroll { get; set; } = true;

    public void Append(string line) => _pending.Enqueue(line);

    public void Clear()
    {
        while (_pending.TryDequeue(out _))
        {
        }

        _lines.Clear();
        Text.Text = string.Empty;
    }

    private void Flush()
    {
        if (_pending.IsEmpty)
        {
            return;
        }

        var appended = new StringBuilder();
        var appendedCount = 0;
        var oldLineCount = _lines.Count;
        var removedLine = false;
        while (_pending.TryDequeue(out var line))
        {
            if (oldLineCount + appendedCount > 0)
            {
                appended.Append('\n');
            }

            appended.Append(line);
            appendedCount++;
            _lines.AddLast(line);
            if (_lines.Count > MaxLines)
            {
                _lines.RemoveFirst();
                removedLine = true;
            }
        }

        Text.Text = removedLine
            ? string.Join('\n', _lines)
            : string.Concat(Text.Text, appended.ToString());
        if (AutoScroll)
        {
            Scroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
        }
    }
}
