namespace NeSud.Core;

public sealed record OnlineGameView(Guid Id, long Revision, int[] Givens, int[] Values, int[] Notes, bool[] Wrong,
    int[] Remaining, int Seconds, int CurrentScore, int Lives, int HintCredits, int CandidateCredits, int Selected,
    bool Paused, bool Completed, bool OutOfLives, bool FinalDefeat, int ReviveSeconds, bool CanUndo, bool CanRedo,
    bool AutoFinish, string? ChallengeDay, bool Empty = false, OnlineRewardView? Reward = null, int Mistakes = 0, int Hints = 0, double ReviveRemainingMs = 0,
    Difficulty Difficulty = global::NeSud.Core.Difficulty.Medium, int CandidateDelta = 0, int LifeRestores = 0, long LifeRestorePenalty = 0, long NextReviveCost = 100);
public sealed record OnlineRewardView(string Kind, int Remaining);
