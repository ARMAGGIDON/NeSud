using NeSud.Core;

namespace NeSud.Server;

public sealed record NewGameRequest(Difficulty Difficulty, Guid? Id, long Revision, bool AutoFinish = false, string? ChallengeDay = null);
public sealed record MoveRequest(Guid Id, long Revision, string Action, int Cell = 0, int Digit = 0, bool Note = false, string? Reward = null, string? ChallengeDay = null);
