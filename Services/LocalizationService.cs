using System.Windows;
using System.Windows.Controls;

namespace SnapPin.Services;

internal static class LocalizationService
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Dictionaries =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["简体中文"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Capture it. Keep it in sight."] = "截图并置顶，随时查看。",
                ["A local-first screenshot and floating-reference tool for Windows."] = "本地优先的 Windows 截图与浮动参考工具。",
                ["Capture"] = "截图", ["Pin"] = "贴图", ["Control"] = "控制", ["Overview"] = "概览", ["Pins"] = "贴图",
                ["Capture shortcut"] = "截图快捷键", ["Save shortcut"] = "保存快捷键", ["More settings"] = "更多设置", ["History"] = "历史记录",
                ["General"] = "常规", ["Toolbar"] = "工具栏", ["Recording"] = "录制", ["Output"] = "输出", ["Hotkeys"] = "快捷键", ["About"] = "关于",
                ["Application"] = "应用程序", ["Interface language"] = "界面语言", ["Cancel"] = "取消", ["Save changes"] = "保存更改",
                ["Search title, tag, text, or barcode"] = "搜索标题、标签、文字或条码", ["Favorites only"] = "仅收藏", ["Recycle bin"] = "回收站",
                ["Check now"] = "立即检查", ["Check for updates when SnapPin starts"] = "SnapPin 启动时检查更新"
            },
            ["繁體中文"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Capture it. Keep it in sight."] = "擷取並置頂，隨時查看。",
                ["A local-first screenshot and floating-reference tool for Windows."] = "本機優先的 Windows 擷取與浮動參考工具。",
                ["Capture"] = "擷取", ["Pin"] = "釘選", ["Control"] = "控制", ["Overview"] = "概覽", ["Pins"] = "釘選",
                ["Capture shortcut"] = "擷取快速鍵", ["Save shortcut"] = "儲存快速鍵", ["More settings"] = "更多設定", ["History"] = "歷史記錄",
                ["General"] = "一般", ["Toolbar"] = "工具列", ["Recording"] = "錄製", ["Output"] = "輸出", ["Hotkeys"] = "快速鍵", ["About"] = "關於",
                ["Application"] = "應用程式", ["Interface language"] = "介面語言", ["Cancel"] = "取消", ["Save changes"] = "儲存變更",
                ["Search title, tag, text, or barcode"] = "搜尋標題、標籤、文字或條碼", ["Favorites only"] = "僅收藏", ["Recycle bin"] = "資源回收筒",
                ["Check now"] = "立即檢查", ["Check for updates when SnapPin starts"] = "SnapPin 啟動時檢查更新"
            }
        };

    internal static string Normalize(string? language) => language is "简体中文" or "繁體中文" ? language : "English";

    internal static string Translate(string text, string? language)
    {
        var normalized = Normalize(language);
        return Dictionaries.TryGetValue(normalized, out var dictionary) && dictionary.TryGetValue(text, out var translated)
            ? translated : text;
    }

    internal static void Apply(DependencyObject root, string? language)
    {
        var normalized = Normalize(language);
        ApplyElement(root, normalized);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) Apply(child, normalized);
    }

    private static void ApplyElement(DependencyObject value, string language)
    {
        if (value is TextBlock text && !string.IsNullOrWhiteSpace(text.Text)) text.Text = Translate(text.Text, language);
        if (value is HeaderedContentControl headered && headered.Header is string header) headered.Header = Translate(header, language);
        if (value is ContentControl content && content.Content is string label) content.Content = Translate(label, language);
        if (value is FrameworkElement element && element.ToolTip is string tip) element.ToolTip = Translate(tip, language);
    }
}
