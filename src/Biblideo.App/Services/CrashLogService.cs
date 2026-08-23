using System.IO;

namespace Biblideo.App.Services;

public static class CrashLogService
{
    private static readonly object SyncRoot = new();

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Ideo Solutions",
        "Biblideo",
        "Logs");

    public static string Write(string context, Exception exception)
    {
        lock (SyncRoot)
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"Biblideo_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(
                path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}");
            return path;
        }
    }
}
