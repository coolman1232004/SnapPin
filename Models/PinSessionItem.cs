namespace SnapPin.Models;

internal sealed class PinSessionItem
{
    public string FileName { get; set; } = string.Empty;
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Opacity { get; set; } = 1;
    public bool Topmost { get; set; } = true;
    public string Group { get; set; } = "Default";
    public string Background { get; set; } = "Transparent";
    public string DesktopId { get; set; } = string.Empty;
    public string HistoryRecordId { get; set; } = string.Empty;
    public bool TextSelectable { get; set; }
    public string OcrText { get; set; } = string.Empty;
    public string OcrBarcodeText { get; set; } = string.Empty;
    public string OcrBarcodeFormat { get; set; } = string.Empty;
    public List<RecognizedWord> OcrWords { get; set; } = [];
    public string Title { get; set; } = string.Empty;
    public bool PositionLocked { get; set; }
    public bool WindowShaded { get; set; }
    public bool EdgeSnapping { get; set; } = true;
    public string MonitorDeviceName { get; set; } = string.Empty;
    public int PhysicalLeft { get; set; }
    public int PhysicalTop { get; set; }
    public int PhysicalWidth { get; set; }
    public int PhysicalHeight { get; set; }
    public int MonitorLeft { get; set; }
    public int MonitorTop { get; set; }
    public int MonitorWidth { get; set; }
    public int MonitorHeight { get; set; }
    public double LongScrollOffset { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool ClickThrough { get; set; }
}
