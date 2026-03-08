using OpenpilotSdk.Hardware;

namespace OpenpilotToolkit.Avalonia.ViewModels;

public sealed class DeviceViewModel(OpenpilotDevice model) : ViewModelBase
{
    public OpenpilotDevice Model { get; } = model ?? throw new ArgumentNullException(nameof(model));

    public string DisplayName => $"{GetDeviceTypeLabel(Model.DeviceType)}  {Model.IpAddress}";

    public string HostLine => string.IsNullOrWhiteSpace(Clean(Model.HostName))
        ? "Hostname unavailable"
        : $"Host: {Clean(Model.HostName)}";

    public string StatusLine => Model.IsAuthenticated
        ? $"Authenticated over SSH on port {Model.Port}"
        : $"Reachable on port {Model.Port}; SSH authentication not confirmed yet";

    public string DetailLine => $"{GetDeviceTypeLabel(Model.DeviceType)} at {Model.IpAddress}";

    public string SummaryLine => $"{HostLine}. {StatusLine}.";

    public void Refresh()
    {
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(SummaryLine));
    }

    private static string Clean(string? value)
    {
        return value?.Trim('\0', '\r', '\n', ' ') ?? string.Empty;
    }

    private static string GetDeviceTypeLabel(OpenpilotDeviceType deviceType)
    {
        return deviceType switch
        {
            OpenpilotDeviceType.Comma2 => "Comma 2",
            OpenpilotDeviceType.Comma3 => "Comma 3",
            OpenpilotDeviceType.Comma3X => "Comma 3X",
            _ => "Unknown Device"
        };
    }
}
