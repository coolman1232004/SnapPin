namespace SnapPin.Services;

internal readonly record struct AnnotationToolbarDefinition(string Key, string Label);

internal static class AnnotationToolbarCatalog
{
    internal static readonly AnnotationToolbarDefinition[] All =
    [
        new("Rectangle", "Rectangle"),
        new("Ellipse", "Ellipse"),
        new("Arrow", "Arrow"),
        new("Line", "Straight line"),
        new("Pencil", "Pencil"),
        new("Marker", "Marker"),
        new("Blur", "Blur brush"),
        new("Text", "Text"),
        new("Eraser", "Eraser brush"),
        new("Magnify", "Magnifier")
    ];

    internal static readonly string[] DefaultEnabled =
    [
        "Rectangle", "Ellipse", "Arrow", "Line", "Pencil", "Marker", "Blur", "Text", "Eraser", "Magnify"
    ];

    internal static List<string> NormalizeOrder(IEnumerable<string>? order)
    {
        var known = All.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = (order ?? [])
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        result.AddRange(All.Select(item => item.Key).Where(key => !result.Contains(key, StringComparer.OrdinalIgnoreCase)));
        return result;
    }

    internal static List<string> NormalizeEnabled(IEnumerable<string>? enabled)
    {
        var known = All.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = (enabled ?? [])
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (result.Count == 0) result.Add("Rectangle");
        return result;
    }
}

internal static class CaptureToolbarCatalog
{
    internal static readonly AnnotationToolbarDefinition[] All =
    [
        new("Copy", "Copy"),
        new("Pin", "Pin"),
        new("PinThumbnail", "Pin as thumbnail"),
        new("Save", "Save as image"),
        new("QuickSave", "Quick save"),
        new("LongCapture", "Long scrolling capture"),
        new("Record", "Screen recording"),
        new("OCR", "OCR / QR recognition"),
        new("Recapture", "Capture another area"),
        new("Cancel", "Cancel capture")
    ];

    internal static List<string> NormalizeOrder(IEnumerable<string>? order)
    {
        var known = All.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = (order ?? [])
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        result.AddRange(All.Select(item => item.Key).Where(key => !result.Contains(key, StringComparer.OrdinalIgnoreCase)));
        return result;
    }

    internal static List<string> NormalizeEnabled(IEnumerable<string>? enabled)
    {
        var known = All.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = (enabled ?? [])
            .Where(known.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (result.Count == 0) result.Add("Copy");
        return result;
    }
}
