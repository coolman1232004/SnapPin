using SnapPin.Models;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SnapPin.Services;

internal sealed record PinSessionSnapshot(BitmapSource Image, PinSessionItem Item);

internal static class PinSessionService
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapPin", "PinSession");
    private static readonly object QueueSync = new();
    private static PendingWrite? _pending;
    private static Task _worker = Task.CompletedTask;
    private static bool _workerRunning;
    private static string _lastFingerprint = string.Empty;

    public static string? LastError { get; private set; }

    public static Task QueueSave(IReadOnlyList<PinSessionSnapshot> pins, string fingerprint)
        => Queue(new PendingWrite(pins, fingerprint, Clear: false));

    public static Task QueueClear() => Queue(new PendingWrite([], "__cleared__", Clear: true));

    private static Task Queue(PendingWrite write)
    {
        lock (QueueSync)
        {
            if (write.Fingerprint == _lastFingerprint && _pending is null) return _worker;
            _pending = write;
            if (!_workerRunning)
            {
                _workerRunning = true;
                _worker = Task.Run(DrainQueue);
            }
            return _worker;
        }
    }

    private static void DrainQueue()
    {
        while (true)
        {
            PendingWrite? write;
            lock (QueueSync)
            {
                write = _pending;
                _pending = null;
                if (write is null)
                {
                    _workerRunning = false;
                    return;
                }
            }

            try
            {
                if (write.Clear) ClearCore(Root);
                else SaveCore(Root, write.Pins);
                lock (QueueSync) _lastFingerprint = write.Fingerprint;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }

    internal static void SaveCore(string root, IReadOnlyList<PinSessionSnapshot> pins)
    {
        Directory.CreateDirectory(root);
        var pointerPath = Path.Combine(root, "current.json");
        AtomicFileService.TryReadJson<SessionPointer>(pointerPath, out var previousPointer);
        var generationName = $"gen_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        var generationPath = Path.Combine(root, generationName);
        Directory.CreateDirectory(generationPath);

        try
        {
            var items = new List<PinSessionItem>(pins.Count);
            for (var index = 0; index < pins.Count; index++)
            {
                var snapshot = pins[index];
                snapshot.Item.FileName = $"pin_{index:D4}.png";
                CaptureService.SavePng(snapshot.Image, Path.Combine(generationPath, snapshot.Item.FileName));
                items.Add(snapshot.Item);
            }
            AtomicFileService.WriteJson(Path.Combine(generationPath, "session.json"), items);
            if (LoadGeneration(root, generationName) is not { } verified || verified.Count != items.Count)
                throw new InvalidDataException("The new pin-session backup could not be verified.");

            var pointer = new SessionPointer(generationName, previousPointer?.Current);
            AtomicFileService.WriteJson(pointerPath, pointer);
            CleanupOldGenerations(root, pointer);
        }
        catch
        {
            TryDeleteDirectory(generationPath);
            throw;
        }
    }

    public static IReadOnlyList<(BitmapSource Image, PinSessionItem Item)> Load() => LoadCore(Root);

    internal static IReadOnlyList<(BitmapSource Image, PinSessionItem Item)> LoadCore(string root)
    {
        try
        {
            var pointerPath = Path.Combine(root, "current.json");
            if (AtomicFileService.TryReadJson<SessionPointer>(pointerPath, out var pointer) && pointer is not null)
            {
                foreach (var generation in new[] { pointer.Current, pointer.Previous }.Where(name => !string.IsNullOrWhiteSpace(name)))
                    if (LoadGeneration(root, generation!) is { } loaded) return loaded;
            }

            if (Directory.Exists(root))
            {
                foreach (var directory in Directory.EnumerateDirectories(root, "gen_*").OrderByDescending(Directory.GetCreationTimeUtc))
                    if (LoadGeneration(root, Path.GetFileName(directory)) is { } recovered) return recovered;
            }

            return LoadLegacy(root);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<(BitmapSource Image, PinSessionItem Item)>? LoadGeneration(string root, string generationName)
    {
        if (!IsSafeGenerationName(generationName)) return null;
        var generationPath = Path.Combine(root, generationName);
        var indexPath = Path.Combine(generationPath, "session.json");
        if (!AtomicFileService.TryReadJson<List<PinSessionItem>>(indexPath, out var items) || items is null) return null;
        var result = new List<(BitmapSource, PinSessionItem)>(items.Count);
        foreach (var item in items)
        {
            var path = SafePinPath(generationPath, item.FileName);
            if (path is null || !File.Exists(path)) return null;
            result.Add((LoadBitmap(path), item));
        }
        return result;
    }

    private static IReadOnlyList<(BitmapSource Image, PinSessionItem Item)> LoadLegacy(string root)
    {
        var indexPath = Path.Combine(root, "session.json");
        if (!AtomicFileService.TryReadJson<List<PinSessionItem>>(indexPath, out var items) || items is null) return [];
        var result = new List<(BitmapSource, PinSessionItem)>();
        foreach (var item in items)
        {
            var path = SafePinPath(root, item.FileName);
            if (path is null || !File.Exists(path)) continue;
            result.Add((LoadBitmap(path), item));
        }
        return result;
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string? SafePinPath(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName) return null;
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static bool IsSafeGenerationName(string name)
        => name.StartsWith("gen_", StringComparison.Ordinal) && Path.GetFileName(name) == name;

    private static void CleanupOldGenerations(string root, SessionPointer pointer)
    {
        var keep = new HashSet<string>(new[] { pointer.Current, pointer.Previous }.Where(name => !string.IsNullOrWhiteSpace(name))!, StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(root, "gen_*"))
            if (!keep.Contains(Path.GetFileName(directory))) TryDeleteDirectory(directory);
        foreach (var legacy in Directory.EnumerateFiles(root, "pin_*.png"))
            try { File.Delete(legacy); } catch (IOException) { }
        var legacyIndex = Path.Combine(root, "session.json");
        try { if (File.Exists(legacyIndex)) File.Delete(legacyIndex); } catch (IOException) { }
    }

    internal static void ClearCore(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record PendingWrite(IReadOnlyList<PinSessionSnapshot> Pins, string Fingerprint, bool Clear);
    private sealed record SessionPointer(string Current, string? Previous);
}
