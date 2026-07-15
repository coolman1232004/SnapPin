using System.IO;
using System.Text;
using System.Text.Json;

namespace SnapPin.Services;

internal static class AtomicFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void WriteJson<T>(string path, T value) => WriteText(path, JsonSerializer.Serialize(value, JsonOptions));

    public static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporary, path, BackupPath(path), ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
                File.Copy(path, BackupPath(path), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static bool TryReadJson<T>(string path, out T? value)
    {
        if (TryReadJsonFile(path, out value)) return true;
        return TryReadJsonFile(BackupPath(path), out value);
    }

    public static string BackupPath(string path) => path + ".bak";

    private static bool TryReadJsonFile<T>(string path, out T? value)
    {
        value = default;
        if (!File.Exists(path)) return false;
        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            return value is not null;
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
