namespace NeSud.Core;

public sealed record HistoryPage(long Cursor, List<GameRecord> Records);
public sealed record AccumulatedRank(string User, long Wins, long PerfectWins, long CurrentStreak, long BestStreak, long Score);
public sealed record WeeklyRanking(string Week, List<RankEntry> Leaders, string? PreviousWeek, List<RankEntry> PreviousLeaders);
