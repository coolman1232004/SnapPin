namespace SnapPin.Models;

public sealed class CaptureOptions
{
    public int DelaySeconds { get; set; }
    public int FixedWidth { get; set; }
    public int FixedHeight { get; set; }
    public double AspectRatio { get; set; }
    public bool IncludeCursor { get; set; }
}
