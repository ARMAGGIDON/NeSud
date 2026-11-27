using System.Diagnostics;

namespace NeSud.Server;

// Opt-in evidence independent of console rendering, host logging and the CLI wrapper.
// Does not register console signals, cancel requests, or change the host lifetime.
internal sealed class ShutdownTrace
{
    private readonly string path;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly object gate = new();
    private readonly int pid = Environment.ProcessId;

    private ShutdownTrace(string path) => this.path = path;

    internal static ShutdownTrace? Start()
    {
        string? configured = Environment.GetEnvironmentVariable("NESUD_SHUTDOWN_TRACE");
        if (string.IsNullOrWhiteSpace(configured)) return null;
        try
        {
            var trace = new ShutdownTrace(Path.GetFullPath(configured));
            Directory.CreateDirectory(Path.GetDirectoryName(trace.path)!);
            trace.Append("Process.start");
            AppDomain.CurrentDomain.ProcessExit += (_, _) => trace.Write("ProcessExit code=" + Environment.ExitCode);
            return trace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine("Shutdown trace unavailable: " + ex.GetType().Name);
            return null;
        }
    }

    internal void Observe(IHostApplicationLifetime lifetime)
    {
        lifetime.ApplicationStarted.Register(() => Write("ApplicationStarted"));
        lifetime.ApplicationStopping.Register(() => Write("ApplicationStopping"));
        lifetime.ApplicationStopped.Register(() => Write("ApplicationStopped"));
    }

    private void Append(string marker)
    {
        string line = FormattableString.Invariant($"{DateTimeOffset.UtcNow:O} pid={pid} +{clock.Elapsed.TotalMilliseconds:F0}ms {marker}");
        lock (gate) File.AppendAllText(path, line + Environment.NewLine);
    }

    internal void Write(string marker)
    {
        try { Append(marker); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
