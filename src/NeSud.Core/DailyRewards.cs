namespace NeSud.Core;

public sealed record RewardItem(string Kind, int Amount);
public sealed record DailyPack(string Day, string Milestone, int Slot);
public sealed record DailyStatus(string Day, int Wins, int Lives, int Hints, int Candidates, List<DailyPack> Packs);
public sealed record DailyChoice(string Day, string Milestone, int Slot, int Card);
public sealed record DailyResult(RewardItem Item, DailyStatus Status);
public sealed record WalletRequest(Guid Operation, Guid GameId, string Kind);
public sealed record PendingReward(string Scope, WalletRequest Request);
public sealed record WalletResult(Guid Operation, Guid GameId, string Kind);
