namespace NeSud.Core;

public sealed record RewardItem(string Kind, int Amount);
public sealed record DailyPack(string Day, string Milestone, int Slot);
public sealed record DailyStatus(string Day, int Wins, int Lives, int Hints, int Candidates, List<DailyPack> Packs);
public sealed record DailyChoice(string Day, string Milestone, int Slot, int Card);
public sealed record DailyResult(RewardItem Item, DailyStatus Status);
public sealed record WalletRequest(Guid Operation, Guid GameId, string Kind);
public sealed record PendingReward(string Scope, WalletRequest Request);
public sealed record WalletResult(Guid Operation, Guid GameId, string Kind);

public sealed record HistoryPage(long Cursor, List<GameRecord> Records);
public sealed record AccumulatedRank(string User, long Wins, long PerfectWins, long CurrentStreak, long BestStreak, long Score);
public sealed record WeeklyRanking(string Week, List<RankEntry> Leaders, string? PreviousWeek, List<RankEntry> PreviousLeaders);

public sealed record ChallengeDay(string Day, string Status);
public sealed record ChallengeCalendar(string Month, string Today, List<ChallengeDay> Days, List<string> Cups);
public sealed record PublicGame(DateTimeOffset Ended, string Mode, string Source, Difficulty Difficulty, string Outcome,
    int Seconds, int Score, int Mistakes, int Hints, int CandidateUses, int AutoFilled, int LifeRestores, string? ChallengeDay);
public sealed record PublicProfile(string User, int Games, int Wins, int PerfectWins, int CurrentStreak, int BestStreak,
    long Score, int DailyWins, List<string> Cups, List<PublicGame> History, bool HasMore, List<LevelStats>? ByDifficulty = null);
public sealed record OnlineGameView(Guid Id, long Revision, int[] Givens, int[] Values, int[] Notes, bool[] Wrong,
    int[] Remaining, int Seconds, int CurrentScore, int Lives, int HintCredits, int CandidateCredits, int Selected,
    bool Paused, bool Completed, bool OutOfLives, bool FinalDefeat, int ReviveSeconds, bool CanUndo, bool CanRedo,
    bool AutoFinish, string? ChallengeDay, bool Empty = false, OnlineRewardView? Reward = null, int Mistakes = 0, int Hints = 0, double ReviveRemainingMs = 0);
public sealed record OnlineRewardView(string Kind, int Remaining);

public sealed record LevelStats(Difficulty Difficulty, int Games, int Wins, int PerfectWins, int CurrentStreak, int BestStreak, long Score);
public sealed record FriendEntry(string User, string Status);
public sealed record FriendRequest(string User, string Action);
