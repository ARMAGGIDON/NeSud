using System.Security.Cryptography;
using System.Text;
using NeSud.Core;
using NeSud.Storage;

namespace NeSud.Server;

public sealed record RewardTicket(string Kind, DateTimeOffset ReadyAt, DateTimeOffset ExpiresAt);
public sealed record MoveAudit(int Moves = 0, int FastIntervals = 0, DateTimeOffset? LastMove = null)
{
    public MoveAudit Record(DateTimeOffset now) => this with
    { Moves = Moves + 1, FastIntervals = FastIntervals + (LastMove is { } last && (now - last).TotalMilliseconds < 90 ? 1 : 0), LastMove = now };
    public bool Flag(Game game) => Moves >= 16 && (FastIntervals >= 12 && FastIntervals * 5 >= (Moves - 1) * 4 ||
        game.Seconds * 1000L < Moves * 90L);
}
public sealed record WebGame(Guid Id, DateTimeOffset Started, Game Game, DateTimeOffset LastSeen, long Revision, RewardTicket? Reward = null, MoveAudit? Audit = null, double Fraction = 0, string? ChallengeDay = null);
