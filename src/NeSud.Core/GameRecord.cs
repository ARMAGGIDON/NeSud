namespace NeSud.Core;

public sealed record GameRecord(Guid Id, DateTimeOffset Started, DateTimeOffset Ended, string Source,
    string Outcome, Game Game)
{
    public int Score => Outcome != "completed" ? 0 : (int)Math.Max(0L,
        1000L * ((int)Game.Puzzle.Difficulty + 1) - Game.Seconds / 3L - 75L * Game.Mistakes - 150L * Game.Hints);

    public bool Valid() => Id != Guid.Empty && Started <= Ended && Ended <= DateTimeOffset.UtcNow.AddMinutes(5)
        && Source is "desktop" or "web" && Outcome is "completed" or "abandoned"
        && Game is not null && Game.Valid() && (Outcome == "completed") == Game.Completed;
}

public sealed record AccountSession(string Token, string User);
public sealed record Credentials(string User, string Password, string? Confirmation = null);
public sealed record RankEntry(string User, int Games, long Score);

public sealed record DesktopDraft(Guid Id, DateTimeOffset Started, DateTimeOffset Updated, Game Game);
public sealed record SyncAck(bool Saved);
