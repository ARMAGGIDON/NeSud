using System.Numerics;

namespace NeSud.Core;

public static partial class Sudoku
{
    // 0: naked singles; 1: hidden singles; 2: locked candidates and naked pairs;
    // 3: X-Wing and contradiction proved by singles. No unrestricted search.
    public static int? LogicalDifficulty(int[] givens, CancellationToken token = default)
    {
        if (!IsConsistent(givens)) return null;
        var board = (int[])givens.Clone();
        var masks = Enumerable.Range(0, 81).Select(i => board[i] == 0?Candidates(board, i): 0).ToArray();
        int highest = 0;
        while (board.Contains(0))
        {
            token.ThrowIfCancellationRequested();
            if (Contradiction(board, masks))return null;
            if (Single(board, masks, false))continue;
            if (Single(board, masks, true))
            {
                highest = Math.Max(highest, 1);
                continue;
            }
            if (Locked(board, masks) || Pairs(board, masks))
            {
                highest = Math.Max(highest, 2);
                continue;
            }
            if (Fish(board, masks) || Forcing(board, masks, token))
            {
                highest = 3;
                continue;
            }
            return null;
        }
        return IsConsistent(board)?highest: null;
    }
    public static bool MatchesDifficulty(int[] givens, Difficulty difficulty, CancellationToken token = default)
    {
        var tier = LogicalDifficulty(givens, token);
        return difficulty switch { Difficulty.Easy => tier == 0, Difficulty.Medium => tier is 0 or 1,
            Difficulty.Hard => tier == 2, Difficulty.Expert => tier == 3, _ => false };
    }
    private static bool Remove(int[] board, int[] masks, int cell, int bits)
    {
        if (board[cell] != 0)return false;
        int old = masks[cell];
        masks[cell] &= ~bits;
        return old != masks[cell];
    }
    private static void Place(int[] board, int[] masks, int cell, int digit)
    {
        board[cell] = digit;
        masks[cell] = 0;
        foreach (int p in Peers[cell])Remove(board, masks, p, 1<<digit);
    }
    private static bool Contradiction(int[] board, int[] masks)
    {
        for (int i = 0; i < 81; i++)if (board[i] == 0 && masks[i] == 0)return true;
        foreach (var unit in Units)
        {
            int possible = 0, placed = 0;
            foreach (int i in unit)
            {
                if (board[i] == 0)possible |= masks[i];
                else
                {
                    int bit = 1<<board[i];
                    if ((placed & bit) != 0)return true;
                    placed |= bit;
                }
            }
            if (((possible | placed) & All) != All)return true;
        }
        return false;
    }
    private static bool Single(int[] board, int[] masks, bool hidden)
    {
        if (!hidden)
        {
            for (int i = 0; i < 81; i++)if (board[i] == 0 && BitOperations.PopCount((uint)masks[i]) == 1)
            { Place(board, masks, i, BitOperations.TrailingZeroCount((uint)masks[i]));
            return true; }
            return false;
        }
        foreach (var unit in Units)
        for (int d = 1; d <= 9; d++)
        {
            int cell = -1, count = 0;
            foreach (int i in unit)if (board[i] == 0 && (masks[i] & (1<<d)) != 0)
            {
                cell = i;
                count++;
            }
            if (count == 1)
            {
                Place(board, masks, cell, d);
                return true;
            }
        }
        return false;
    }
    private static bool Locked(int[] board, int[] masks)
    {
        for (int u = 0; u < 27; u++)
        for (int d = 1; d <= 9; d++)
        {
            int bit = 1<<d;
            var candidates = Units[u].Where(i => board[i] == 0 && (masks[i] & bit) != 0).ToArray();
            if (candidates.Length < 2)continue;
            for (int v = 0; v < 27; v++)
            {
                if (u == v || !candidates.All(i => Units[v].Contains(i)))continue;
                bool changed = false;
                foreach (int i in Units[v])if (!Units[u].Contains(i))changed |= Remove(board, masks, i, bit);
                if (changed)return true;
            }
        }
        return false;
    }
    private static bool Pairs(int[] board, int[] masks)
    {
        foreach (var unit in Units)
        foreach (int i in unit)
        {
            int mask = masks[i];
            if (board[i] != 0 || BitOperations.PopCount((uint)mask) != 2)continue;
            var pair = unit.Where(j => board[j] == 0 && masks[j] == mask).ToArray();
            if (pair.Length != 2)continue;
            bool changed = false;
            foreach (int j in unit)if (!pair.Contains(j))changed |= Remove(board, masks, j, mask);
            if (changed)return true;
        }
        return false;
    }
    private static bool Fish(int[] board, int[] masks)
    {
        for (int transpose = 0; transpose < 2; transpose++)
        for (int d = 1; d <= 9; d++)
        {
            int bit = 1<<d;
            int Cell(int r, int c) => transpose == 0?r * 9 + c: c * 9 + r;
            var positions = new int[9];
            for (int r = 0; r < 9; r++)for (int c = 0; c < 9; c++)if ((masks[Cell(r, c)] & bit) != 0)positions[r] |= 1<<c;
            for (int a = 0; a < 9; a++)for (int b = a + 1; b < 9; b++)
            {
                int p = positions[a];
                if (p != positions[b] || BitOperations.PopCount((uint)p) != 2)continue;
                bool changed = false;
                for (int r = 0; r < 9; r++)if (r != a && r != b)
                    for (int c = 0; c < 9; c++)if ((p & (1<<c)) != 0)changed |= Remove(board, masks, Cell(r, c), bit);
                if (changed)return true;
            }
        }
        return false;
    }
    private static bool Forcing(int[] board, int[] masks, CancellationToken token)
    {
        for (int i = 0; i < 81; i++)
        {
            if (board[i] != 0 || BitOperations.PopCount((uint)masks[i]) != 2)continue;
            for (int d = 1; d <= 9; d++)
            {
                int bit = 1<<d;
                if ((masks[i] & bit) == 0)continue;
                token.ThrowIfCancellationRequested();
                var b = (int[])board.Clone();
                var m = (int[])masks.Clone();
                Place(b, m, i, d);
                while (!Contradiction(b, m) && (Single(b, m, false) || Single(b, m, true)))token.ThrowIfCancellationRequested();
                if (Contradiction(b, m))return Remove(board, masks, i, bit);
            }
        }
        return false;
    }
}
