namespace NeSud.Core;

// UI focus is not part of the game snapshot. A valid anchor is kept even when only a digit is highlighted.
public static class BoardFocus
{
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
