using OpenpilotSdk.OpenPilot;

namespace OpenpilotToolkit.Avalonia.ViewModels;

public sealed class RouteViewModel(Route model)
{
    public Route Model { get; } = model ?? throw new ArgumentNullException(nameof(model));

    public string Identifier => Model.ToString();

    public string DateLine => Model.Date.ToLocalTime().ToString("MMM d, yyyy  HH:mm");

    public string SegmentLine => Model.Segments.Count == 1
        ? "1 segment"
        : $"{Model.Segments.Count} segments";
}
