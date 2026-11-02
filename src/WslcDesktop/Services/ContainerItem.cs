using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace WslcDesktop.Services;

/// <summary>A container row that updates in place as state and telemetry change.</summary>
public sealed class ContainerItem(string name) : INotifyPropertyChanged
{
    private string _image = string.Empty;
    private string _state = string.Empty;
    private string _cpu = "–";
    private string _memory = "–";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; } = name;

    public string Image { get => _image; private set => Set(ref _image, value); }

    public string State { get => _state; private set => Set(ref _state, value); }

    public string Cpu { get => _cpu; private set => Set(ref _cpu, value); }

    public string Memory { get => _memory; private set => Set(ref _memory, value); }

    public bool IsRunning => IsRunningState(_state);

    public bool IsStopped => !IsRunning;

    public Visibility RunningVisibility => IsRunning ? Visibility.Visible : Visibility.Collapsed;

    public Visibility StoppedVisibility => IsRunning ? Visibility.Collapsed : Visibility.Visible;

    public static bool IsRunningState(string state) =>
        state.Equals("running", StringComparison.OrdinalIgnoreCase);

    public void Update(ContainerRow row, StatsSample? sample)
    {
        Image = row.Image;
        State = row.State;
        if (IsRunning && sample is not null)
        {
            Cpu = $"{sample.CpuPercent:0.#}%";
            Memory = WslcHost.FormatSize(sample.MemoryBytes);
        }
        else
        {
            Cpu = Memory = "–";
        }
    }

    private void Set(ref string field, string value, [CallerMemberName] string? property = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        if (property == nameof(State))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStopped)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunningVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StoppedVisibility)));
        }
    }
}
