namespace NeSud.Core;

// UI focus is not part of the game snapshot. A valid anchor is kept even when only a digit is highlighted.
public static class BoardFocus
{
    // Remaining uses indices 0..8 for digits 1..9. Wrap from 9 to 1, skipping completed digits.
    public static int NextDigit(int digit, IReadOnlyList<int> remaining)
    {
        if (digit is < 1 or > 9) return 0;
        for (int step = 1; step < 9; step++)
        {
            int next = (digit - 1 + step) % 9 + 1;
            if (remaining[next - 1] > 0) return next;
        }
        return 0;
    }

    public static int Nearest(int[] values, int anchor, int digit, bool skipAnchor = true)
    {
        if (digit is < 1 or > 9 || anchor is < 0 or >= 81) return -1;
        int best = -1, distance = int.MaxValue;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != digit || (skipAnchor && i == anchor)) continue;
            int d = Math.Abs(i / 9 - anchor / 9) + Math.Abs(i % 9 - anchor % 9);
            if (d < distance) { best = i; distance = d; }
        }
        return best >= 0 ? best : values[anchor] == digit ? anchor : -1;
    }
}
