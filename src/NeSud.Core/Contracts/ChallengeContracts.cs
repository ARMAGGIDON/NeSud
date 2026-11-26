namespace NeSud.Core;

public sealed record ChallengeDay(string Day, string Status);
public sealed record ChallengeCalendar(string Month, string Today, List<ChallengeDay> Days, List<string> Cups);
