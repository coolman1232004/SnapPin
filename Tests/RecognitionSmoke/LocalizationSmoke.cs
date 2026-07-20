using SnapPin.Services;
using System.Windows.Controls;
using System.Windows.Data;

namespace SnapPin.RecognitionSmoke;

internal static class LocalizationSmoke
{
    internal static void Run()
    {
        var simplifiedMissing = LocalizationService.MissingTranslations(LocalizationService.SimplifiedChinese);
        var traditionalMissing = LocalizationService.MissingTranslations(LocalizationService.TraditionalChinese);
        if (simplifiedMissing.Count > 0 || traditionalMissing.Count > 0)
            throw new InvalidOperationException(
                $"Missing localized resources: zh-Hans={simplifiedMissing.Count}, zh-Hant={traditionalMissing.Count}");

        if (LocalizationService.Translate("Capture", LocalizationService.SimplifiedChinese) != "截图" ||
            LocalizationService.Translate("General", LocalizationService.TraditionalChinese) != "一般" ||
            LocalizationService.Translate("SnapPin recovered", LocalizationService.SimplifiedChinese) != "SnapPin 已恢复" ||
            LocalizationService.Translate("The update was cancelled.", LocalizationService.TraditionalChinese) != "更新已取消。")
            throw new InvalidOperationException("One or more localized resource lookups returned the wrong value.");

        var controlResult = Program.RunSta(() =>
        {
            var model = new LocalizationBindingProbe { Value = "Live value" };
            var bound = new TextBlock { DataContext = model };
            bound.SetBinding(TextBlock.TextProperty, new Binding(nameof(LocalizationBindingProbe.Value)));
            var staticText = new TextBlock { Text = "Configuration storage" };
            var check = new CheckBox { Content = "Run SnapPin on system startup" };
            var panel = new StackPanel();
            panel.Children.Add(bound);
            panel.Children.Add(staticText);
            panel.Children.Add(check);
            LocalizationService.Apply(panel, LocalizationService.SimplifiedChinese);
            return (BoundText: bound.Text, Binding: BindingOperations.GetBindingExpression(bound, TextBlock.TextProperty),
                StaticText: staticText.Text, Check: check.Content as string);
        });
        if (controlResult.BoundText != "Live value" || controlResult.Binding is null ||
            controlResult.StaticText != "配置存储" || controlResult.Check != "系统启动时运行 SnapPin")
            throw new InvalidOperationException("Automatic localization damaged a binding or missed a static control.");

        Console.WriteLine($"LOCALIZATION RESOURCES: {simplifiedMissing.Count + traditionalMissing.Count} missing; bindings preserved");
    }

    private sealed class LocalizationBindingProbe
    {
        public string Value { get; init; } = string.Empty;
    }
}
