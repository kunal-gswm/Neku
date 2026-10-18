using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using DesktopPet.Native;

namespace DesktopPet.Services.DeskSense;

/// <summary>
/// Default system signal provider querying standard Win32 APIs.
/// </summary>
public sealed class WindowsDeskSenseSignalProvider : IDeskSenseSignalProvider
{
    public (string ProcessName, string WindowTitle) GetForegroundApp()
    {
        try
        {
            IntPtr hWnd = Win32.GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return ("", "");

            Win32.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return ("", "");

            using var process = Process.GetProcessById((int)pid);
            string procName = process.ProcessName.ToLowerInvariant();
            string title = Win32.GetWindowTitle(hWnd);

            return (procName, title);
        }
        catch
        {
            return ("", "");
        }
    }

    public double GetIdleSeconds()
    {
        try
        {
            var plii = new Win32.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Win32.LASTINPUTINFO>() };
            if (Win32.GetLastInputInfo(ref plii))
            {
                uint elapsedMs = unchecked((uint)Environment.TickCount - plii.dwTime);
                return elapsedMs / 1000.0;
            }
        }
        catch { }
        return 0.0;
    }

    public bool IsPresentationOrQuietMode()
    {
        try
        {
            var state = Win32.GetUserNotificationState();
            return state switch
            {
                UserNotificationState.PresentationMode => true,
                UserNotificationState.RunningD3dFullScreen => true,
                UserNotificationState.Busy => true,
                UserNotificationState.QuietTime => true,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    public DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;
}

/// <summary>
/// DeskSense context engine: non-intrusively monitors local desktop activity,
/// categorizes foreground applications, detects user idle states, and generates
/// debounced transitions for companion and radar integration.
/// </summary>
public sealed class DeskSenseService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly IDeskSenseSignalProvider _signals;
    private readonly DispatcherTimer? _timer;
    private bool _running;
    private bool _disposed;

    // Known application recognition catalogs
    private static readonly HashSet<string> CodingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "devenv", "idea64", "idea", "studio64", "studio",
        "rider64", "rider", "pycharm64", "sublime_text", "cursor", "clion64", "webstorm64"
    };

    private static readonly HashSet<string> TerminalApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "windowsterminal", "wt", "powershell", "pwsh", "cmd",
        "bash", "mintty", "conemu64", "conemu", "alacritty", "wezterm-gui"
    };

    private static readonly HashSet<string> BrowserApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "arc", "opera", "vivaldi"
    };

    private static readonly HashSet<string> MeetingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "zoom", "teams", "ms-teams", "webex", "slack", "discord"
    };

    public DeskSenseContext CurrentContext { get; private set; } = new();

    // Debounce & state tracking
    private DeskSenseAppCategory _pendingCategory = DeskSenseAppCategory.Other;
    private string _pendingProcessName = "";
    private int _consecutiveCategoryMatches;
    private const int DebounceSamplesRequired = 2; // ~2-3 seconds at 1.5s sampling

    private bool _wasIdle;
    private bool _wasPresenting;
    private DeskSenseAppCategory _lastStableCategory = DeskSenseAppCategory.Other;

    public event Action<DeskSenseContext>? ContextUpdated;
    public event Action<DeskSenseTransition, DeskSenseContext>? TransitionOccurred;

    public DeskSenseService(AppSettings settings, IDeskSenseSignalProvider? signalProvider = null, Dispatcher? dispatcher = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _signals = signalProvider ?? new WindowsDeskSenseSignalProvider();

        if (dispatcher != null || Dispatcher.FromThread(Thread.CurrentThread) != null)
        {
            var targetDispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
            _timer = new DispatcherTimer(DispatcherPriority.Background, targetDispatcher)
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _timer.Tick += (_, _) => Poll();
        }
    }

    public void Start()
    {
        if (_running || _disposed) return;
        _running = true;
        _timer?.Start();
        Poll();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();
    }

    /// <summary>
    /// Explicitly evaluates desktop signals and calculates transitions.
    /// Safe to call synchronously or in unit tests.
    /// </summary>
    public DeskSenseContext Poll()
    {
        if (!_settings.EnableDeskSense)
        {
            return CurrentContext;
        }

        double idleSeconds = _settings.EnableIdleDetection ? _signals.GetIdleSeconds() : 0.0;
        bool isIdle = _settings.EnableIdleDetection && idleSeconds >= _settings.IdleThresholdSeconds;
        bool isPresenting = _signals.IsPresentationOrQuietMode();

        string procName = "";
        string winTitle = "";

        if (_settings.EnableForegroundAppDetection && !isIdle)
        {
            var app = _signals.GetForegroundApp();
            procName = app.ProcessName;
            winTitle = app.WindowTitle;
        }

        var detectedCategory = isIdle
            ? DeskSenseAppCategory.Idle
            : CategorizeProcess(procName);

        var snapshot = new DeskSenseContext
        {
            ProcessName = procName,
            WindowTitle = winTitle,
            Category = detectedCategory,
            IsIdle = isIdle,
            IdleSeconds = idleSeconds,
            IsPresentingOrQuiet = isPresenting,
            Timestamp = _signals.GetUtcNow()
        };

        CurrentContext = snapshot;
        ContextUpdated?.Invoke(snapshot);

        EvaluateTransitions(snapshot);

        return snapshot;
    }

    public static DeskSenseAppCategory CategorizeProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return DeskSenseAppCategory.Other;

        string clean = processName.Trim().ToLowerInvariant();

        if (CodingApps.Contains(clean)) return DeskSenseAppCategory.Coding;
        if (TerminalApps.Contains(clean)) return DeskSenseAppCategory.Terminal;
        if (BrowserApps.Contains(clean)) return DeskSenseAppCategory.Browser;
        if (MeetingApps.Contains(clean)) return DeskSenseAppCategory.Meeting;

        return DeskSenseAppCategory.Other;
    }

    private void EvaluateTransitions(DeskSenseContext context)
    {
        // 1. Idle Transitions (immediate based on threshold, no debouncing needed for user return)
        if (context.IsIdle && !_wasIdle)
        {
            _wasIdle = true;
            TransitionOccurred?.Invoke(DeskSenseTransition.UserIdle, context);
            return;
        }
        else if (!context.IsIdle && _wasIdle)
        {
            _wasIdle = false;
            TransitionOccurred?.Invoke(DeskSenseTransition.UserReturned, context);
            _lastStableCategory = context.Category;
            return;
        }

        if (context.IsIdle)
        {
            return; // Stay idle until user returns
        }

        // 2. Presentation Mode Transitions
        if (context.IsPresentingOrQuiet && !_wasPresenting)
        {
            _wasPresenting = true;
            TransitionOccurred?.Invoke(DeskSenseTransition.PresentationStarted, context);
        }
        else if (!context.IsPresentingOrQuiet)
        {
            _wasPresenting = false;
        }

        // 3. Application Category Debouncing
        if (context.Category == _pendingCategory && string.Equals(context.ProcessName, _pendingProcessName, StringComparison.OrdinalIgnoreCase))
        {
            _consecutiveCategoryMatches++;
        }
        else
        {
            _pendingCategory = context.Category;
            _pendingProcessName = context.ProcessName;
            _consecutiveCategoryMatches = 1;
        }

        // Only transition once the category remains stable for the debounce threshold
        if (_consecutiveCategoryMatches >= DebounceSamplesRequired)
        {
            if (_pendingCategory != _lastStableCategory)
            {
                var prev = _lastStableCategory;
                _lastStableCategory = _pendingCategory;

                if (_pendingCategory == DeskSenseAppCategory.Coding)
                {
                    TransitionOccurred?.Invoke(DeskSenseTransition.CodingStarted, context);
                }
                else if (prev == DeskSenseAppCategory.Coding && _pendingCategory != DeskSenseAppCategory.Coding)
                {
                    TransitionOccurred?.Invoke(DeskSenseTransition.CodingPaused, context);
                }
                else if (_pendingCategory == DeskSenseAppCategory.Terminal)
                {
                    TransitionOccurred?.Invoke(DeskSenseTransition.TerminalActive, context);
                }
                else
                {
                    TransitionOccurred?.Invoke(DeskSenseTransition.DevelopmentActivityChanged, context);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
