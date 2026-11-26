using System.Numerics;

namespace NeSud.Core;

public enum Difficulty { Easy, Medium, Hard, Expert }
public sealed record Puzzle(int[] Givens, int[] Solution, Difficulty Difficulty);

public static partial class Sudoku
{
    public const int All = 0x3FE;
    public static readonly int[][] Units =
        Enumerable.Range(0, 9).Select(r => Enumerable.Range(0, 9).Select(c => r * 9 + c).ToArray())
        .Concat(Enumerable.Range(0, 9).Select(c => Enumerable.Range(0, 9).Select(r => r * 9 + c).ToArray()))
        .Concat(Enumerable.Range(0, 9).Select(b => Enumerable.Range(0, 9)
            .Select(k => (b / 3 * 3 + k / 3) * 9 + b % 3 * 3 + k % 3).ToArray())).ToArray();
    public static readonly int[][] Peers = Enumerable.Range(0, 81).Select(i =>
        Units.Where(u => u.Contains(i)).SelectMany(u => u).Where(j => j != i).Distinct().ToArray()).ToArray();

    public static int Candidates(int[] board, int i)
    {
        int mask = All;
        foreach (int j in Peers[i]) mask &= ~(1 << board[j]);
        return mask;
    }

    public static bool IsConsistent(int[] board) => board.Length == 81 &&
        board.All(v => v is >= 0 and <= 9) &&
        Units.All(u => u.Select(i => board[i]).Where(v => v != 0).Distinct().Count() ==
                       u.Count(i => board[i] != 0));

    public static int CountSolutions(int[] board, int limit = 2, CancellationToken token = default)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (!IsConsistent(board)) return 0;
        return Search((int[])board.Clone(), limit, null, token);
    }

    private static int Search(int[] board, int limit, Random? rng, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int cell = -1, best = 10, mask = 0;
        for (int i = 0; i < 81; i++)
        {
            if (board[i] != 0) continue;
            int m = Candidates(board, i), n = BitOperations.PopCount((uint)m);
            if (n == 0) return 0;
            if (n >= best) continue;
            (cell, best, mask) = (i, n, m);
            if (n == 1) break;
        }
        if (cell < 0) return 1;
        var digits = Enumerable.Range(1, 9).Where(d => (mask & (1 << d)) != 0).ToArray();
        rng?.Shuffle(digits.AsSpan());
        int count = 0;
        foreach (int d in digits)
        {
            board[cell] = d;
            count += Search(board, limit - count, rng, token);
            if (count >= limit)
            {
                if (rng is null) board[cell] = 0;
                return count;
            }
        }
        board[cell] = 0;
        return count;
    }

    public static (int Cell, int Digit, string Technique)? LogicalMove(int[] board)
    {
        if (!IsConsistent(board)) return null;
        var masks = Enumerable.Range(0, 81).Select(i => board[i] == 0 ? Candidates(board, i) : 0).ToArray();
        for (int i = 0; i < 81; i++)
            if (BitOperations.PopCount((uint)masks[i]) == 1)
                return (i, BitOperations.TrailingZeroCount((uint)masks[i]), "Єдиний кандидат");
        foreach (var unit in Units)
            for (int d = 1; d <= 9; d++)
            {
                int cell = -1, count = 0;
                foreach (int i in unit)
                    if ((masks[i] & (1 << d)) != 0)
                    {
                        cell = i;
                        count++;
                    }
                if (count == 1) return (cell, d, "Прихований одинак");
            }
        return null;
    }

    public static bool SolvableBySingles(int[] givens)
    {
        var board = (int[])givens.Clone();
        while (LogicalMove(board) is { } move) board[move.Cell] = move.Digit;
        return board.All(v => v != 0) && IsConsistent(board);
    }

    // Difficulty is a reproducible combination of clue bands and basic logical solvability.
    public static (int Min, int Max) ClueBand(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => (42, 46), Difficulty.Medium => (34, 38),
        Difficulty.Hard => (28, 32), Difficulty.Expert => (24, 27),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
    };

    public static Puzzle Generate(Difficulty difficulty, CancellationToken token = default, Random? random = null)
    {
        var rng = random ?? Random.Shared;
        var (min, max) = ClueBand(difficulty);
        for (int attempt = 0; attempt < 1200; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var solution = new int[81];
            if (Search(solution, 1, rng, token) != 1) continue;
            var givens = (int[])solution.Clone();
            var order = Enumerable.Range(0, 81).ToArray();
            rng.Shuffle(order.AsSpan());
            int clues = 81, target = rng.Next(min, max + 1);
            foreach (int i in order)
            {
                if (clues <= target) break;
                int previous = givens[i];
                givens[i] = 0;
                if (CountSolutions(givens, 2, token) == 1) clues--;
                else givens[i] = previous;
            }
            if (clues < min || clues > max) continue;
            if (!MatchesDifficulty(givens, difficulty, token)) continue;
            return new Puzzle(givens, solution, difficulty);
        }
        throw new InvalidOperationException("Не вдалося згенерувати поле. Спробуйте ще раз.");
    }
}
