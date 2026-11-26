namespace NeSud.Core;

public sealed record PublicGame(DateTimeOffset Ended, string Mode, string Source, Difficulty Difficulty, string Outcome,
    int Seconds, int Score, int Mistakes, int Hints, int CandidateUses, int AutoFilled, int LifeRestores, string? ChallengeDay);
public sealed record PublicProfile(string User, int Games, int Wins, int PerfectWins, int CurrentStreak, int BestStreak,
    long Score, int DailyWins, List<string> Cups, List<PublicGame> History, bool HasMore, List<LevelStats>? ByDifficulty = null, bool Verified = false, string? Avatar = null);

public sealed record LevelStats(Difficulty Difficulty, int Games, int Wins, int PerfectWins, int CurrentStreak, int BestStreak, long Score);
