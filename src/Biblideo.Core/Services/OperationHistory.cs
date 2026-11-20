using System.Text;
using System.Text.Json;

namespace Biblideo.Core.Services;

public static class OperationHistory
{
    public static Task AppendAsync<T>(string path, T entry, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Le chemin de l’historique est invalide.");
        Directory.CreateDirectory(directory);
        return File.AppendAllTextAsync(path, JsonSerializer.Serialize(entry) + Environment.NewLine, cancellationToken);
    }

    public static IEnumerable<string> ReadEntries(string path)
    {
        StringBuilder? formattedEntry = null;
        foreach (var line in File.ReadLines(path))
        {
            // Les anciennes versions écrivaient un objet JSON indenté par événement.
            if (line == "{")
            {
                formattedEntry = new StringBuilder().AppendLine(line);
                continue;
            }

            if (line.StartsWith('{'))
            {
                formattedEntry = null;
                yield return line;
                continue;
            }

            if (formattedEntry is null) continue;
            formattedEntry.AppendLine(line);
            if (line != "}") continue;

            yield return formattedEntry.ToString();
            formattedEntry = null;
        }
    }
}
