using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Biblideo.App.Services;

/// <summary>
/// Single-executable updater, aligned with Idéo Launcher: GitHub Release discovery,
/// download, integrity check when GitHub provides a digest, replacement and restart.
/// </summary>
public sealed class ApplicationUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/lrcstephane-wq/biblideo/releases/latest";
    private const string AssetName = "Biblideo.exe";
    private readonly HttpClient _httpClient = new();
    private ReleaseAsset? _availableAsset;
    private string? _availableVersion;

    public ApplicationUpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Biblideo-Updater/1.1");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string CurrentVersion => GetCurrentVersion().ToString(3);
    public string? AvailableVersion => _availableVersion;

    public async Task<string?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _availableAsset = null;
            _availableVersion = null;
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken)
                      ?? throw new InvalidOperationException("La réponse GitHub ne contient aucune information de version.");

        var versionText = release.TagName.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var latestVersion))
            throw new InvalidOperationException($"La version publiée « {release.TagName} » est invalide.");

        if (latestVersion <= GetCurrentVersion())
        {
            _availableAsset = null;
            _availableVersion = null;
            return null;
        }

        _availableAsset = release.Assets.FirstOrDefault(asset =>
            asset.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"La version {release.TagName} ne contient pas {AssetName}.");
        _availableVersion = latestVersion.ToString(3);
        return _availableVersion;
    }

    public async Task DownloadAndRestartAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_availableAsset is null || _availableVersion is null)
            throw new InvalidOperationException("Aucune mise à jour n’a été détectée.");

        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
            throw new InvalidOperationException("Le fichier exécutable actuel est introuvable.");

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ideo Solutions", "Biblideo", "Updates", _availableVersion);
        Directory.CreateDirectory(updateDirectory);
        var downloadedExecutable = Path.Combine(updateDirectory, AssetName);

        using var response = await _httpClient.GetAsync(
            _availableAsset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var destination = new FileStream(downloadedExecutable, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
        {
            var buffer = new byte[1024 * 128];
            long downloaded = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (totalBytes is > 0) progress?.Invoke((int)Math.Clamp(downloaded * 100 / totalBytes.Value, 0, 100));
            }
        }

        VerifyDigestIfPresent(downloadedExecutable, _availableAsset.Digest);
        progress?.Invoke(100);
        StartReplacementScript(currentExecutable, downloadedExecutable);
        Environment.Exit(0);
    }

    private static Version GetCurrentVersion()
    {
        var value = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return new Version(value.Major, value.Minor, Math.Max(0, value.Build));
    }

    private static void VerifyDigestIfPresent(string path, string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return;
        var expected = digest["sha256:".Length..].Trim();
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le contrôle d’intégrité de la mise à jour a échoué. Le fichier téléchargé a été refusé.");
    }

    private static void StartReplacementScript(string currentExecutable, string downloadedExecutable)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"Biblideo_Update_{Guid.NewGuid():N}.cmd");
        var processId = Environment.ProcessId;
        var script = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("setlocal")
            .AppendLine($"for /l %%i in (1,1,45) do (")
            .AppendLine($"  tasklist /FI \"PID eq {processId}\" 2>NUL | find \"{processId}\" >NUL")
            .AppendLine("  if errorlevel 1 goto replace")
            .AppendLine("  timeout /t 1 /nobreak >NUL")
            .AppendLine(")")
            .AppendLine(":replace")
            .AppendLine($"copy /y \"{downloadedExecutable}\" \"{currentExecutable}\" >NUL")
            .AppendLine("if errorlevel 1 exit /b 1")
            .AppendLine($"start \"\" \"{currentExecutable}\"")
            .AppendLine("del \"%~f0\"")
            .ToString();
        File.WriteAllText(scriptPath, script, Encoding.ASCII);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("assets")] IReadOnlyList<ReleaseAsset> Assets);

    private sealed record ReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest);
}
