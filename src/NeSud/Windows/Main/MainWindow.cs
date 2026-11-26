using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NeSud.Core;
using NeSud.Storage;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;

namespace NeSud;

public sealed partial class MainWindow : Window
{
    private readonly LocalStore store = new(LocalStore.DefaultPath, StateStore.DefaultPath);
    private readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };
    private AccountSession? account;
    private bool syncing, reachable, probing;
    private DateTimeOffset nextProbe = DateTimeOffset.MinValue;
    private readonly string serverUrl = ServiceUrl();
    private DateTimeOffset nextSync = DateTimeOffset.MinValue;
    private string Scope => state.AccountScope ?? "";
    private readonly AppState state;
    private readonly DispatcherTimer timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };
    private readonly Stopwatch clock = new();
    private readonly Button[] cells = new Button[81], digits = new Button[9], levels = new Button[4];
    private Button undo = null!, redo = null!, erase = null!, hint = null!, notes = null!, fill = null!,
        pause = null!, restart = null!, newGame = null!, theme = null!;
    private Grid board = null!, cover = null!;
    private Border resultCard = null!, playCard = null!, sideCard = null!, menuCard = null!;
    private bool inMenu = true;
    private Button continueMenu = null!, menuNavigation = null!, accountNavigation = null!;
    private TextBlock resultTitle = null!, resultSummary = null!, resultBreakdown = null!, resultComparison = null!;
    private Guid shownResult;
    private bool defeatSaved;
    private CheckBox autoFinish = null!;
    private TextBlock time = null!, errors = null!, progress = null!, heading = null!, status = null!,
        coverTitle = null!, coverDetail = null!, stats = null!, saveStatus = null!;
    private ProgressBar progressBar = null!;
    private Button coverButton = null!;
    private Difficulty nextDifficulty;
    private CancellationTokenSource? generation;
    private int lastSavedSecond, accountedSeconds;
    private bool closing, watchingReward, spending;
    private DailyStatus? daily;
    private readonly Dictionary<string, long> historyCursors = new();
    private readonly Dictionary<string, string> draftFingerprints = new();
    private Button resultDaily = null!;
    private bool weeklyRewards, challengeRewards;
    private TextBlock lives = null!, score = null!, quest = null!;
    private Button rewardLife = null!, rewardHint = null!, rewardCandidates = null!;
    private Game? G => state.Game;
    private bool Busy => generation is not null;
    private bool Playing => !inMenu && G is { Paused: false, Completed: false, OutOfLives: false } && !Busy && !watchingReward && state.PendingReward is null;

    public MainWindow()
    {
        state = store.Load();
        state.Game?.UpgradeRules();
        if (G is { Completed: false, OutOfLives: false }) G.Paused = true;
        if (G?.ReviveReadyAt is not null) G.CancelRevive();
        if (G is { Completed: false }) state.AutoFinish = G.AutoFinish;
        // A legacy life purchase cannot be used outside the new last-chance rule.
        if (state.PendingReward?.Request.Kind == "life") state.PendingReward = null;
        account = RestoreSession();
        if (state.Game is not null && state.GameId == Guid.Empty)
        {
            state.GameId = Guid.NewGuid();
            state.Started = DateTimeOffset.UtcNow;
        }
        nextDifficulty = state.NextDifficulty ?? G?.Puzzle.Difficulty ?? Difficulty.Medium;
        Title = "NeSud • Судоку";
        MinWidth = 790;
        MinHeight = 620;
        Width = Math.Clamp(state.Width, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Clamp(state.Height, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        ApplyTheme();
        Build();
        if (state.Maximized) WindowState = WindowState.Maximized;
        PreviewKeyDown += OnKey;
        timer.Tick += (_, _) =>
        {
            ReadClock();
            if (G is { OutOfLives: true } && !Busy)
            {
                UpdateDefeat();
                if (!watchingReward && G.FinalDefeat(DateTimeOffset.UtcNow) && !defeatSaved)
                {
                    defeatSaved = true;
                    Render();
                    Save();
                }
            }
            if (G is not null && G.Seconds - lastSavedSecond >= 5) Save();
            if (account is not null && !syncing && DateTimeOffset.UtcNow >= nextSync) _ = Sync();
            if (!probing && DateTimeOffset.UtcNow >= nextProbe) _ = Probe();
        };
        timer.Start();
        Closing += (_, _) =>
        {
            closing = true;
            CloseSurfaces();
            generation?.Cancel();
            ReadClock();
            clock.Stop();
            timer.Stop();
            state.Width = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width;
            state.Height = RestoreBounds.Height > 0 ? RestoreBounds.Height : Height;
            state.Maximized = WindowState == WindowState.Maximized;
            Save();
        };
        Loaded += (_, _) =>
        {
            SyncClock();
            Render();
            FocusCell();
            if (store.Warning is { } warning) status.Text = warning;
        };
        Render();
    }
}
