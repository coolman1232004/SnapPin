using System.IO;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace SnapAnchor.Services;

internal static class MediaTrimService
{
    public static async Task<string> TrimMp4Async(string inputPath, TimeSpan trimFromStart, TimeSpan trimFromEnd, CancellationToken cancellationToken)
    {
        if (trimFromStart <= TimeSpan.Zero && trimFromEnd <= TimeSpan.Zero) return inputPath;
        var input = await StorageFile.GetFileFromPathAsync(inputPath);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(inputPath)!);
        var temporaryName = $"{Path.GetFileNameWithoutExtension(inputPath)}.trim-{Guid.NewGuid():N}.mp4";
        var output = await folder.CreateFileAsync(temporaryName, CreationCollisionOption.ReplaceExisting);
        var backupPath = inputPath + $".pretrim-{Guid.NewGuid():N}.bak";
        var replaced = false;
        try
        {
            var transcoder = new MediaTranscoder
            {
                TrimStartTime = trimFromStart,
                TrimStopTime = trimFromEnd,
                HardwareAccelerationEnabled = true,
                AlwaysReencode = true
            };
            var prepared = await transcoder.PrepareFileTranscodeAsync(input, output, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto));
            if (!prepared.CanTranscode)
                throw new InvalidOperationException(LocalizationService.Format("Windows could not trim this recording ({0}).", prepared.FailureReason));
            cancellationToken.ThrowIfCancellationRequested();
            await prepared.TranscodeAsync();
            cancellationToken.ThrowIfCancellationRequested();

            // Both files are in the same directory, so File.Replace keeps the original
            // intact unless the replacement succeeds completely.
            File.Replace(output.Path, inputPath, backupPath, ignoreMetadataErrors: true);
            replaced = true;
            if (File.Exists(backupPath)) File.Delete(backupPath);
            return inputPath;
        }
        finally
        {
            if (File.Exists(output.Path)) File.Delete(output.Path);
            if (replaced && File.Exists(inputPath) && File.Exists(backupPath)) File.Delete(backupPath);
        }
    }
}
