namespace NeSud.Core;

public sealed record GameRecord(Guid Id, DateTimeOffset Started, DateTimeOffset Ended, string Source,
    string Outcome, Game Game)
{
    public int Score => Outcome == "completed" ? Game.CurrentScore : 0;

    public bool Valid() => Id != Guid.Empty && Started <= Ended && Ended <= DateTimeOffset.UtcNow.AddMinutes(5)
        && Source is "desktop" or "web" && Outcome is "completed" or "abandoned" or "defeated"
        && Game is not null && Game.Valid() && (Outcome == "completed") == Game.Completed && (Outcome != "defeated" || Game.OutOfLives);
}

public sealed record AccountSession(string Token, string User);
public sealed record Credentials(string User, string Password, string? Confirmation = null);
public sealed record RankEntry(string User, int Games, long Score);

public sealed record DesktopDraft(Guid Id, DateTimeOffset Started, DateTimeOffset Updated, Game Game);
public sealed record SyncAck(bool Saved);

public sealed record DifficultyComparison(int Games, int Players, double AverageSeconds, double MedianSeconds,
    int BestSeconds, double AverageScore, double AverageMistakes, double AverageHints, double AverageCandidates,
    int ScorePercentile, int TimePercentile);
