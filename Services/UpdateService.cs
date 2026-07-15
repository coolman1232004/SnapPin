using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO;

namespace SnapPin.Services;

internal sealed record UpdateCheckResult(bool UpdateAvailable, string Message, string DownloadUrl = "", string Version = "", string Sha256 = "");

internal static class UpdateService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    internal static async Task<UpdateCheckResult> CheckAsync(string? feedUrl, CancellationToken cancellationToken = default)
    {
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(feedUrl))
            return new UpdateCheckResult(false, $"SnapPin {current.ToString(3)} is installed. Add an HTTPS JSON update feed URL to check published releases.");
        if (!Uri.TryCreate(feedUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return new UpdateCheckResult(false, "The update feed must be a valid HTTPS URL.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"SnapPin/{current.ToString(3)}");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken);
        if (manifest is null || !Version.TryParse(manifest.Version, out var published))
            return new UpdateCheckResult(false, "The update feed did not contain a valid version.");
        return published > current
            ? new UpdateCheckResult(true, $"SnapPin {published.ToString(3)} is available.\n\n{manifest.ReleaseNotes}".Trim(),
                manifest.DownloadUrl ?? string.Empty, published.ToString(3), manifest.InstallerSha256 ?? string.Empty)
            : new UpdateCheckResult(false, $"SnapPin {current.ToString(3)} is up to date.");
    }

    internal static async Task<string> DownloadInstallerAsync(UpdateCheckResult update, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The installer URL must use HTTPS.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapPin", "Updates");
        Directory.CreateDirectory(root);
        var version = string.IsNullOrWhiteSpace(update.Version) ? "update" : update.Version;
        var finalPath = Path.Combine(root, $"SnapPin-Setup-{version}.exe");
        var temporaryPath = finalPath + ".download";
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            long received = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                received += count;
                if (total > 0) progress?.Report(received / (double)total.Value);
            }
        }
        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            await using var stream = File.OpenRead(temporaryPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actual.Equals(update.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
                throw new InvalidDataException("The downloaded installer failed its SHA-256 integrity check.");
            }
        }
        File.Move(temporaryPath, finalPath, true);
        return finalPath;
    }

    internal static async Task DownloadAndLaunchAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
    {
        var installer = await DownloadInstallerAsync(update, cancellationToken: cancellationToken);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer) { UseShellExecute = true });
    }

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? InstallerSha256 { get; set; }
    }
}
