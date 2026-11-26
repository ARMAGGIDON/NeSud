using Microsoft.Extensions.Configuration;

namespace NeSud.Server;

/// <summary>Loads only SMTP settings from a local dotenv file, without changing process environment.</summary>
public static class SmtpEnv
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Smtp__Host"] = "Smtp:Host", ["SMTP_HOST"] = "Smtp:Host",
        ["Smtp__Port"] = "Smtp:Port", ["SMTP_PORT"] = "Smtp:Port",
        ["Smtp__From"] = "Smtp:From", ["SMTP_FROM"] = "Smtp:From",
        ["Smtp__User"] = "Smtp:User", ["SMTP_USER"] = "Smtp:User",
        ["Smtp__Password"] = "Smtp:Password", ["SMTP_PASSWORD"] = "Smtp:Password"
    };

    public static void AddTo(IConfigurationBuilder configuration, string contentRoot)
    {
        if (Find(contentRoot) is { } path)
            configuration.AddInMemoryCollection(Read(path));
    }

    private static string? Find(string contentRoot)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(contentRoot));
        string local = Path.Combine(directory.FullName, ".env");
        if (File.Exists(local)) return local;

        // Only a recognized NeSud repository root may supply a fallback file.
        for (DirectoryInfo? parent = directory; parent is not null; parent = parent.Parent)
        {
            if (!File.Exists(Path.Combine(parent.FullName, "NeSud.slnx"))) continue;
            string root = Path.Combine(parent.FullName, ".env");
            return File.Exists(root) ? root : null;
        }

        return null;
    }

    private static Dictionary<string, string?> Read(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        int lineNumber = 0;
        foreach (string source in File.ReadLines(path))
        {
            lineNumber++;
            string line = source.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            int separator = line.IndexOf('=');
            if (separator < 0)
            {
                if (Keys.ContainsKey(line)) throw InvalidLine(lineNumber);
                continue;
            }
            if (!Keys.TryGetValue(line[..separator].Trim(), out string? key)) continue;
            if (!TryValue(line[(separator + 1)..].Trim(), out string value))
                throw InvalidLine(lineNumber);
            values[key] = value;
        }
        return values;
    }

    private static bool TryValue(string source, out string value)
    {
        value = "";
        if (source.Length == 0) return true;
        char quote = source[0];
        if (quote is '\'' or '"')
        {
            int end = source.IndexOf(quote, 1);
            if (end < 0) return false;
            string remainder = source[(end + 1)..].TrimStart();
            if (remainder.Length != 0 && !remainder.StartsWith('#')) return false;
            value = source[1..end];
            return true;
        }

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '#' && (i == 0 || char.IsWhiteSpace(source[i - 1])))
            {
                source = source[..i];
                break;
            }
        }
        value = source.TrimEnd();
        return true;
    }

    // Do not include source text or credentials in diagnostics.
    private static FormatException InvalidLine(int line) =>
        new($"Invalid SMTP setting in .env at line {line}.");
}
