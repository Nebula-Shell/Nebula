namespace CaelestiaWin.Core.Models;

public sealed record ScreenCaptureResult(
    byte[] PngBytes,
    int Width,
    int Height,
    DateTimeOffset CapturedAt);
