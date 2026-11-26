using System.Text.Json;

namespace NeSud.Core;

public sealed class Statistics
{
    public int Wins { get; set; }
    public int? BestSeconds { get; set; }
}

public sealed class AppState
{
    public int Version { get; set; } = 1;
    public Game? Game { get; set; }
    public Difficulty? NextDifficulty { get; set; }
    public bool Dark { get; set; } = true;
    public bool ShowMistakes { get; set; } = true;
    public bool AutoClean { get; set; } = true;
    public bool HighlightPeers { get; set; } = true;
    public double Width { get; set; } = 1080;
    public double Height { get; set; } = 780;
    public bool Maximized { get; set; }
    public Dictionary<Difficulty, Statistics> Stats { get; set; } = [];
}

public sealed class StateStore(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeSud", "state.json");
    public string? Warning { get; private set; }

    public AppState Load()
    {
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(candidate));
                if (state is null || state.Version != 1 || state.Stats is null ||
                    state.NextDifficulty is Difficulty difficulty && !Enum.IsDefined(difficulty) ||
                    state.Stats.Any(p => !Enum.IsDefined(p.Key) || p.Value is null ||
                        p.Value.Wins < 0 || p.Value.BestSeconds is < 0) ||
                    !double.IsFinite(state.Width) || !double.IsFinite(state.Height) ||
                    state.Game is not null && !state.Game.Valid())
                    throw new InvalidDataException("Некоректний стан гри.");
                return state;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or
                                       InvalidDataException or NotSupportedException)
            { Warning = "Не вдалося прочитати збереження: " + e.Message; }
        }
        return new AppState();
    }

    public void Save(AppState state)
    {
        string temp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
        else File.Move(temp, path);
    }
}
