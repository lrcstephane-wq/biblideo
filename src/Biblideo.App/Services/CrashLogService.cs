using System.Diagnostics;
using System.IO;
using Biblideo.Core.Models;

namespace Biblideo.App.Services;

public static class CrashLogService
{
    private static readonly object SyncRoot = new();

    public static string SharedLogDirectory => Path.Combine(
        NexusPreviewProfile.Enabled ? NexusPreviewProfile.DataRoot : SettingsDefaults.ApplicationDataRoot, "Logs");
    public static string LocalFallbackDirectory => NexusPreviewProfile.Enabled
        ? Path.Combine(Path.GetTempPath(), "Ideo Nexus Preview", "Logs")
        : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Biblideo",
        "Logs");

    public static string Write(string context, Exception exception)
    {
        lock (SyncRoot)
        {
            Exception? lastError = null;
            foreach (var directory in new[] { SharedLogDirectory, LocalFallbackDirectory })
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    var machine = SanitizeFileName(Environment.MachineName);
                    var path = Path.Combine(directory, $"Biblideo_{machine}_{DateTime.Now:yyyyMMdd}.log");
                    File.AppendAllText(
                        path,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
                    return path;
                }
                catch (Exception writeError) when (writeError is UnauthorizedAccessException or IOException)
                {
                    lastError = writeError;
                }
            }

            throw new IOException("Aucun emplacement de journal n’est accessible.", lastError);
        }
    }

    public static bool TryOpen(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}
