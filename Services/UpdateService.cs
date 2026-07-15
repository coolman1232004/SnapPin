using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SnapPin.Services;

internal enum UpdatePackageKind
{
    Installer,
    Portable
}

internal sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string Message,
    string DownloadUrl = "",
    string Version = "",
    string Sha256 = "",
    UpdatePackageKind PackageKind = UpdatePackageKind.Installer);

internal static class UpdateService
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SnapPin";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static bool IsPortableInstallation()
    {
        var executable = Environment.ProcessPath;
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        var registered = key?.GetValue("InstallLocation") as string;
        return IsPortableLocation(executable, registered);
    }

    internal static bool IsPortableLocation(string? executable, string? registeredInstallDirectory)
    {
        if (string.IsNullOrWhiteSpace(executable)) return true;
        var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(executable));
        if (string.IsNullOrWhiteSpace(currentDirectory) || string.IsNullOrWhiteSpace(registeredInstallDirectory)) return true;
        return !Path.GetFullPath(currentDirectory).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(registeredInstallDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<UpdateCheckResult> CheckAsync(string? feedUrl, CancellationToken cancellationToken = default)
    {
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(feedUrl))
            return new UpdateCheckResult(false, LocalizationService.Format("SnapPin {0} is installed. The official GitHub update source is unavailable.", current.ToString(3)));
        if (!Uri.TryCreate(feedUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return new UpdateCheckResult(false, LocalizationService.Current("The official update source is not a valid HTTPS address."));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"SnapPin/{current.ToString(3)}");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, ManifestJsonOptions, cancellationToken);
        if (manifest is null || !Version.TryParse(manifest.Version, out var published))
            return new UpdateCheckResult(false, LocalizationService.Current("The GitHub release did not contain a valid version."));
        if (published <= current)
            return new UpdateCheckResult(false, LocalizationService.Format("SnapPin {0} is up to date.", current.ToString(3)));

        var portable = IsPortableInstallation();
        var packageKind = portable ? UpdatePackageKind.Portable : UpdatePackageKind.Installer;
        var downloadUrl = portable
            ? ResolvePackageUrl(uri, manifest.PortableDownloadUrl, manifest.PortableFile)
            : ResolvePackageUrl(uri, manifest.DownloadUrl, manifest.InstallerFile);
        var sha256 = portable ? manifest.PortableSha256 : manifest.InstallerSha256;
        var edition = LocalizationService.Current(portable ? "portable copy" : "installed copy");
        var notes = LocalizationService.CurrentLanguage == LocalizationService.English
            ? manifest.ReleaseNotes
            : LocalizationService.Current("See the GitHub release page for full release notes.");
        var message = $"{LocalizationService.Format("SnapPin {0} is available for this {1}.", published.ToString(3), edition)}\n\n{notes}".Trim();
        if (string.IsNullOrWhiteSpace(downloadUrl))
            message += "\n\n" + LocalizationService.Current("The required update package is not attached to this GitHub release.");
        return new UpdateCheckResult(true, message, downloadUrl, published.ToString(3), sha256 ?? string.Empty, packageKind);
    }

    internal static async Task<string> DownloadPackageAsync(UpdateCheckResult update, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The GitHub update package must use HTTPS.");
        var root = UpdateRoot();
        Directory.CreateDirectory(root);
        var version = string.IsNullOrWhiteSpace(update.Version) ? "update" : update.Version;
        var fileName = update.PackageKind == UpdatePackageKind.Portable
            ? $"SnapPin-Portable-{version}.zip"
            : $"SnapPin-Setup-{version}.exe";
        var finalPath = Path.Combine(root, fileName);
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
                throw new InvalidDataException("The downloaded GitHub package failed its SHA-256 integrity check.");
            }
        }
        File.Move(temporaryPath, finalPath, true);
        return finalPath;
    }

    internal static async Task DownloadAndLaunchAsync(UpdateCheckResult update, CancellationToken cancellationToken = default)
    {
        var package = await DownloadPackageAsync(update, cancellationToken: cancellationToken);
        if (update.PackageKind == UpdatePackageKind.Portable)
        {
            await LaunchPortableUpdaterAsync(package, update.Version, cancellationToken);
            return;
        }
        Process.Start(new ProcessStartInfo(package) { UseShellExecute = true });
    }

    internal static string ResolvePackageUrl(Uri feedUri, string? explicitUrl, string? packageFile)
    {
        if (Uri.TryCreate(explicitUrl, UriKind.Absolute, out var absolute) && absolute.Scheme == Uri.UriSchemeHttps)
            return absolute.AbsoluteUri;
        if (string.IsNullOrWhiteSpace(packageFile)) return string.Empty;
        return new Uri(feedUri, packageFile.Trim()).AbsoluteUri;
    }

    private static async Task LaunchPortableUpdaterAsync(string archivePath, string expectedVersion,
        CancellationToken cancellationToken)
    {
        var targetDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetPathRoot(targetDirectory)?.TrimEnd(Path.DirectorySeparatorChar);
        if (targetDirectory.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A portable update cannot replace files in a drive root.");

        var updateRoot = UpdateRoot();
        var stagingDirectory = Path.Combine(updateRoot, $"Portable-{expectedVersion}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            await ExtractArchiveSafelyAsync(archivePath, stagingDirectory, cancellationToken);
            var stagedExecutable = Path.Combine(stagingDirectory, "SnapPin.exe");
            if (!File.Exists(stagedExecutable))
                throw new InvalidDataException("The portable GitHub package did not contain SnapPin.exe.");

            var backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SnapPin", "PortableRollback", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            var scriptPath = Path.Combine(updateRoot, $"apply-portable-{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(scriptPath, PortableUpdaterScript, new UTF8Encoding(false), cancellationToken);

            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell)) powershell = "powershell.exe";
            var startInfo = new ProcessStartInfo(powershell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = updateRoot
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-ParentPid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("-SourceDirectory");
            startInfo.ArgumentList.Add(stagingDirectory);
            startInfo.ArgumentList.Add("-TargetDirectory");
            startInfo.ArgumentList.Add(targetDirectory);
            startInfo.ArgumentList.Add("-BackupDirectory");
            startInfo.ArgumentList.Add(backupDirectory);
            startInfo.ArgumentList.Add("-ExpectedVersion");
            startInfo.ArgumentList.Add(expectedVersion);
            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("Windows could not start the portable update helper.");
        }
        catch
        {
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true); } catch { }
            throw;
        }
    }

    private static async Task ExtractArchiveSafelyAsync(string archivePath, string destination,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        await using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The portable update contained an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var source = entry.Open();
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(output, cancellationToken);
        }
    }

    private static string UpdateRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapPin", "Updates");

    private const string PortableUpdaterScript = """
param(
    [Parameter(Mandatory=$true)][int]$ParentPid,
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$TargetDirectory,
    [Parameter(Mandatory=$true)][string]$BackupDirectory,
    [Parameter(Mandatory=$true)][string]$ExpectedVersion,
    [switch]$NoRestart
)
$ErrorActionPreference = 'Stop'
$updateRoot = Split-Path -Parent $PSCommandPath
$logPath = Join-Path $updateRoot 'portable-update.log'
function Write-UpdateLog([string]$Message) {
    Add-Content -LiteralPath $logPath -Value (('{0:O}  {1}' -f [DateTime]::UtcNow, $Message)) -Encoding UTF8
}
try {
    Write-UpdateLog "Waiting for SnapPin process $ParentPid to close."
    try { Wait-Process -Id $ParentPid -Timeout 60 -ErrorAction Stop } catch {
        if (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) { throw 'SnapPin did not close in time.' }
    }
    $sourceRoot = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\')
    $targetRoot = [IO.Path]::GetFullPath($TargetDirectory).TrimEnd('\')
    if ($targetRoot -eq [IO.Path]::GetPathRoot($targetRoot).TrimEnd('\')) { throw 'Refusing to update a drive root.' }
    $sourceExe = Join-Path $sourceRoot 'SnapPin.exe'
    $targetExe = Join-Path $targetRoot 'SnapPin.exe'
    if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'The staged SnapPin.exe is missing.' }
    $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse)
    $backupFiles = Join-Path $BackupDirectory 'Files'
    New-Item -ItemType Directory -Path $backupFiles -Force | Out-Null
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $existing = Join-Path $targetRoot $relative
        if (Test-Path -LiteralPath $existing -PathType Leaf) {
            $backup = Join-Path $backupFiles $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
            Copy-Item -LiteralPath $existing -Destination $backup -Force
        }
    }
    Set-Content -LiteralPath (Join-Path $BackupDirectory 'rollback.txt') -Value "Portable SnapPin backup before $ExpectedVersion`r`nRestore target: $targetRoot" -Encoding UTF8
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $destination = Join-Path $targetRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    if (-not (Test-Path -LiteralPath $targetExe -PathType Leaf)) { throw 'The updated SnapPin.exe is missing.' }
    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($targetExe).FileVersion
    if ($ExpectedVersion -and -not $actualVersion.StartsWith($ExpectedVersion + '.')) { throw "Updated version $actualVersion did not match $ExpectedVersion." }
    Write-UpdateLog "Portable update to $ExpectedVersion completed. Backup: $BackupDirectory"
    if (-not $NoRestart) { Start-Process -FilePath $targetExe -WorkingDirectory $targetRoot }
    Remove-Item -LiteralPath $sourceRoot -Recurse -Force -ErrorAction SilentlyContinue
}
catch {
    Write-UpdateLog ("Portable update failed: " + $_.Exception.Message)
    $backupFiles = Join-Path $BackupDirectory 'Files'
    if (Test-Path -LiteralPath $backupFiles) {
        foreach ($file in @(Get-ChildItem -LiteralPath $backupFiles -File -Recurse)) {
            $relative = $file.FullName.Substring($backupFiles.Length).TrimStart('\')
            $destination = Join-Path $TargetDirectory $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
    $targetExe = Join-Path $TargetDirectory 'SnapPin.exe'
    if (-not $NoRestart -and (Test-Path -LiteralPath $targetExe)) { Start-Process -FilePath $targetExe -WorkingDirectory $TargetDirectory }
}
""";

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public string? PortableDownloadUrl { get; set; }
        public string? InstallerFile { get; set; }
        public string? PortableFile { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? InstallerSha256 { get; set; }
        public string? PortableSha256 { get; set; }
    }
}
