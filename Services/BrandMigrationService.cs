using Microsoft.Win32;
using System.IO;

namespace SnapAnchor.Services;

internal static class BrandMigrationService
{
    private const string CurrentName = "SnapAnchor";
    private static readonly string LegacyName = string.Concat("Snap", "Pin");

    internal static void MigrateLocalData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var source = Path.Combine(local, LegacyName);
        var target = Path.Combine(local, CurrentName);
        try
        {
            if (Directory.Exists(source))
                CopyDirectory(source, target);

            MigrateSettingsProperty(target);
            MigrateStartupEntry();
            RemoveLegacyExecutableBesideCurrentProcess();
        }
        catch
        {
            // A brand migration must never prevent capture from starting. If a
            // file is locked, the new product simply starts with fresh data.
        }
    }

    private static void MigrateSettingsProperty(string target)
    {
        var path = Path.Combine(target, "settings.json");
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path);
        var legacyProperty = $"\"Exclude{LegacyName}FromCapture\"";
        const string currentProperty = "\"ExcludeSnapAnchorFromCapture\"";
        if (!text.Contains(legacyProperty, StringComparison.Ordinal)) return;
        File.WriteAllText(path, text.Replace(legacyProperty, currentProperty, StringComparison.Ordinal));
    }

    private static void MigrateStartupEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key?.GetValue(CurrentName) is null && key?.GetValue(LegacyName) is string command)
            key.SetValue(CurrentName, command.Replace(LegacyName, CurrentName, StringComparison.OrdinalIgnoreCase));
        key?.DeleteValue(LegacyName, false);
    }

    private static void RemoveLegacyExecutableBesideCurrentProcess()
    {
        var legacyExecutable = Path.Combine(AppContext.BaseDirectory, LegacyName + ".exe");
        if (!File.Exists(legacyExecutable) ||
            legacyExecutable.Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
        File.Delete(legacyExecutable);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(destination))
                File.Copy(file, destination, overwrite: false);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
