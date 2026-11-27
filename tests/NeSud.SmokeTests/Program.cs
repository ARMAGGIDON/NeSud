using NeSud.Core;
using NeSud.Storage;
using NeSud.Server;
using System.Numerics;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    public static void Main(string[] args)
    {
        int repetitions = args.Length > 0 ? int.Parse(args[0]) : 3;
        if (repetitions < 1) throw new ArgumentOutOfRangeException(nameof(repetitions));
        var random = new Random(2036);
        var puzzles = GeneratePuzzles(repetitions, random);
        TestGame(puzzles);
        TestStorage(puzzles);
        TestSchemaMigration(puzzles[0]);
        TestRules(puzzles);
        TestRewards(puzzles, random);
        TestChallenges(puzzles);
        TestSocial(puzzles);
        TestIdentity();
        TestSmtpEnv();
    }

    static void Check(bool result, string message)
    {
        if (!result) throw new Exception(message);
    }
}
