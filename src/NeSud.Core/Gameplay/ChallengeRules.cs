namespace NeSud.Core;

/// <summary>Rules specific to the daily calendar mode.</summary>
public static class ChallengeRules
{
    public static Difficulty DifficultyForRoll(int roll) => roll switch
    {
        0 => Difficulty.Medium,
        1 => Difficulty.Hard,
        2 => Difficulty.Expert,
        _ => throw new ArgumentOutOfRangeException(nameof(roll))
    };

    public static int RewardQuantity(int roll) => roll switch
    {
        >= 0 and < 65 => 1,
        >= 65 and < 95 => 2,
        >= 95 and < 100 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(roll))
    };

    public static RewardItem[] CreateRewardCards(Func<int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        bool singleType = next(100) < 5;
        string first = next(2) == 0 ? "hint" : "candidates";
        string second = singleType ? first : first == "hint" ? "candidates" : "hint";
        string third = singleType ? first : next(2) == 0 ? "hint" : "candidates";

        RewardItem[] cards =
        [
            new(first, RewardQuantity(next(100))),
            new(second, RewardQuantity(next(100))),
            new(third, RewardQuantity(next(100)))
        ];

        for (int i = cards.Length - 1; i > 0; i--)
        {
            int j = next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        return cards;
    }
}
