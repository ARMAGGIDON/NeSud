using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static List<Puzzle> GeneratePuzzles(int repetitions, Random random)
    {
    var puzzles = new List<Puzzle > ();
        foreach (var level in Enum.GetValues<Difficulty > ())
        {
            for (int i = 0; i < repetitions; i++)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var puzzle = Sudoku.Generate(level, timeout.Token, random);
                var (min, max) = Sudoku.ClueBand(level);
                int clues = puzzle.Givens.Count(v => v != 0);
                Check(clues >= min && clues <= max, "Clue band");
                Check(Sudoku.IsConsistent(puzzle.Solution) && !puzzle.Solution.Contains(0), "Complete valid solution");
                Check(Sudoku.CountSolutions(puzzle.Givens, token: timeout.Token) == 1, "Unique puzzle");
                Check(Sudoku.MatchesDifficulty(puzzle.Givens, level, timeout.Token), "Logical strategy certificate");
                Check(Game.Start(puzzle).Valid(), "Initial game validation");
                if (level is Difficulty.Easy or Difficulty.Medium)
                    Check(Sudoku.SolvableBySingles(puzzle.Givens), "Solvable by singles");
                if (level == Difficulty.Expert)
                    Check(!Sudoku.SolvableBySingles(puzzle.Givens), "Expert exceeds singles");
                puzzles.Add(puzzle);
                Console.WriteLine($"PASS {level}: {clues} clues");
            }
        }
        Check(puzzles.Select(p => string.Join("", p.Givens)).Distinct().Count() == puzzles.Count, "Variety");
        return puzzles;
    }
}
