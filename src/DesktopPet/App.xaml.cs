using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Native;
using DesktopPet.Services;
using DesktopPet.Services.Agent;
using DesktopPet.Services.DeskSense;
using DesktopPet.Services.Radar;
using DesktopPet.UI;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace DesktopPet;

public partial class App : Application
{
    public static App? CurrentApp => Application.Current as App;

    private Mutex? _singleInstance;
    private SettingsService _settingsService = new();
    private AppSettings _settings = new();
    private PetWindow? _petWindow;
    private PetEngine? _engine;
    private TrayService? _tray;
    private SettingsWindow? _settingsWindow;
    private KeyboardMonitor? _keyboard;
    private MouseMonitor? _mouse;
    private SystemMonitor? _system;
    private HookWatchdog? _hookWatchdog;
    private UpdateService? _updateService;
    private DispatcherTimer? _stretchTimer;
    private DispatcherTimer? _stateSaveTimer;
    private DispatcherTimer? _quietWatchTimer;
    private bool _hiddenForQuiet;
    private bool _userHidden;
    private DeveloperEventService? _developerEvents;
    private FocusTimerService _focusTimer = new();
    private FocusHudWindow? _focusHudWindow;
    private NotesService _notesService = new();
    private QuickNotesWindow? _notesWindow;
    private TaskService _taskService = new();
    private TasksWindow? _tasksWindow;
    private FocusHistoryService _focusHistoryService = new();
    private DashboardWindow? _dashboardWindow;
    private ProductivityReminderService? _reminderService;
    private TaskReminderService? _taskReminderService;
    private QuickCaptureWindow? _quickCaptureWindow;
    private DeveloperEventsWindow? _devEventsWindow;
    private CommandPaletteService _paletteService = new();
    private CommandPaletteWindow? _paletteWindow;
    private AgentActivityService _agentActivityService = new();
    private ClaudeCodeIntegrationService _claudeCodeService = new();
    private DeskSenseService? _deskSense;
    private RepositoryMonitorService? _repoMonitor;
    private DeveloperRadarService? _radarService;
    private DeveloperHudWindow? _developerHudWindow;
    private ToyService _toyService = new();
    private readonly List<ToyWindow> _activeToyWindows = new();
    private DesktopPet.Services.Health.SystemHealthService? _healthService;

    private HttpClient? _http;
    private HotkeyService? _hotkey;
    private LauncherIpcService? _ipcServer;
    private readonly Random _rng = new();

    private static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "neku_crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        EnsureInteractiveDesktop();
        DebugLog.Write($"[Lifecycle] OnStartup started with args: [{string.Join(", ", e.Args)}]");

        // Crash diagnostics — capture any unhandled exception with its stack trace.
        // Mark handled so a single stray exception can't kill the user's pet.
        DispatcherUnhandledException += (_, args) =>
        {
            DebugLog.Write($"[Exception] DISPATCHER: {args.Exception}");
            try { File.AppendAllText(CrashLog, $"[{DateTime.Now:HH:mm:ss}] DISPATCHER\n{args.Exception}\n\n"); } catch { }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            DebugLog.Write($"[Exception] DOMAIN: {args.ExceptionObject}");
            try { File.AppendAllText(CrashLog, $"[{DateTime.Now:HH:mm:ss}] DOMAIN\n{args.ExceptionObject}\n\n"); } catch { }
        };

        _singleInstance = new Mutex(initiallyOwned: true, "Neku.SingleInstance", out bool isNew);
        DebugLog.Write($"[Startup] Mutex acquired. isNew={isNew}");
        if (!isNew)
        {
            bool isLauncherStart = e.Args.Contains("--launcher-start") ||
                                   Environment.GetCommandLineArgs().Contains("--launcher-start");

            // Forward activation request to running instance via named pipe (bounded timeout)
            bool signaled = LauncherIpcService.TrySignalRunningInstance(timeoutMs: 800);

            if (isLauncherStart)
            {
                DebugLog.Write($"[Startup] --launcher-start duplicate detected; signaled={signaled}. Exiting cleanly without dialog.");
                Shutdown();
                return;
            }

            if (signaled)
            {
                DebugLog.Write("[Startup] Duplicate instance signaled running instance via IPC. Exiting cleanly.");
                Shutdown();
                return;
            }

            HandleAlreadyRunningInstance();
            Shutdown();
            return;
        }

        // Primary instance starts IPC server for incoming launcher activations
        _ipcServer = new LauncherIpcService();
        _ipcServer.ActivationRequested += () =>
        {
            Dispatcher.Invoke(() =>
            {
                DebugLog.Write("[LauncherIPC] Activation received from launcher.");

                // 1. Respect quiet mode / full-screen suppression rules
                if (_hiddenForQuiet || (_settings.EnableQuietMode && _engine?.IsQuiet == true))
                {
                    DebugLog.Write("[LauncherIPC] Activation suppressed: quiet mode or full-screen application is active.");
                    return;
                }

                // 2. If pet was hidden by the user, restore it to visible state
                if (_userHidden)
                {
                    DebugLog.Write("[LauncherIPC] Pet was hidden by user; unhiding.");
                    OnHideToggled(false);
                    return;
                }

                // 3. If pet is off-screen, clamp it back into visible work area
                if (_engine != null && _engine.IsOffScreen())
                {
                    DebugLog.Write("[LauncherIPC] Pet is off-screen; resetting to visible work area.");
                    _engine.ResetPosition();
                    return;
                }

                // 4. If pet is already visible and normal, play gentle acknowledgement reaction without stealing focus
                if (_engine != null)
                {
                    DebugLog.Write("[LauncherIPC] Pet is already visible; triggering gentle acknowledgement.");
                    _engine.TriggerReaction(PetState.Wave, "👋 Here I am!", cooldownSeconds: 1.0);
                }
            });
        };
        _ipcServer.Start();
        _commandIpcServer = new Neku.Protocol.NekuIpcServer(HandleIpcCommandAsync, logger: msg => DebugLog.Write(msg));
        _commandIpcServer.Start();

        var legacyConflict = CheckLegacyPixelPawsConflict();
        if (legacyConflict != null)
        {
            DebugLog.Write($"[Startup] Legacy PixelPaws conflict detected: {legacyConflict}");
            MessageBox.Show(legacyConflict, "Neku - Legacy Process Detected", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        _settings = _settingsService.Load();

        if (e.Args.Contains("--software-rendering") || _settings.UseSoftwareRendering)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        SpriteAnimator animator;
        try
        {
            animator = LoadPet(_settings.ActivePet);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't load the cat's artwork.\n\n{ex.Message}", "Neku",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _petWindow = new PetWindow();
        _petWindow.Show();

        DebugLog.Write($"Neku {AppVersion.Display} starting. " +
                       $"DpiScale={_petWindow.DpiScale:0.###} " +
                       $"VirtualScreen=({SystemParameters.VirtualScreenLeft},{SystemParameters.VirtualScreenTop}) " +
                       $"{SystemParameters.VirtualScreenWidth}x{SystemParameters.VirtualScreenHeight}");

        // Persistent effects overlay (hearts/sparkles/bubbles) — created here on the UI thread,
        // kept hidden until an effect or speech bubble is actively rendered.
        var overlay = new EffectsOverlay();
        _petWindow.Effects = overlay;

        // Input monitors — installed on the UI thread so the hook callbacks run here.
        _system = new SystemMonitor();
        if (!_settings.DisableOptionalMonitoring)
        {
            _keyboard = new KeyboardMonitor();
            _mouse = new MouseMonitor();
            _hookWatchdog = new HookWatchdog(_keyboard, _mouse, _system);
        }

        var surfaceProvider = new SurfaceProvider(() => _petWindow!.Handle);
        var stateMachine = new StateMachine(_settings, _system);
        _engine = new PetEngine(_petWindow, animator, surfaceProvider, stateMachine, _settings, _keyboard, _mouse, _system, _toyService);
        _petWindow.TargetFps = _settings.TargetFps;
        _petWindow.Attach(_engine, _engine.Width, _engine.Height);
        _engine.TriggerGreeting();

        // Watch for "should the cat be hidden right now?". Deliberately a slow timer rather than
        // engine work: when the answer is yes we detach the render loop completely, and the
        // engine then isn't ticking to notice when the answer changes back.
        _quietWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _quietWatchTimer.Tick += (_, _) => UpdateQuietHiding();
        _quietWatchTimer.Start();

        // Continuity: checkpoint where the cat is and how it feels, so an abrupt shutdown
        // (or a crash) still leaves something close to the truth on disk.
        _stateSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _stateSaveTimer.Tick += (_, _) => SavePetState();
        _stateSaveTimer.Start();

        _focusTimer = new FocusTimerService(Dispatcher);
        _focusHistoryService = new FocusHistoryService();
        _focusTimer.HistoryService = _focusHistoryService;
        _focusTimer.LoadState(_settings);
        _focusTimer.FocusCompleted += OnFocusCompleted;
        _focusTimer.BreakCompleted += OnBreakCompleted;
        _notesService = new NotesService();
        _taskService = new TaskService();
        _taskService.TaskCompleted += OnTaskCompleted;
        _taskService.TaskDeleted += OnTaskDeleted;

        // Initialize local System Health Service
        if (_settings.EnableSystemHealthMonitoring)
        {
            _healthService = new DesktopPet.Services.Health.SystemHealthService(_settings);
            _healthService.AlertTriggered += alert =>
            {
                if (_settings.EnableHealthCompanionAlerts)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (_settings.EnableQuietMode || _hiddenForQuiet || _engine?.IsQuiet == true) return;
                        string line = _engine?.Personality.GetHealthAlertLine(alert.Metric) ?? alert.Message;
                        PetState reaction = alert.Metric switch
                        {
                            DesktopPet.Services.Health.SystemHealthMetric.Cpu => PetState.Surprised,
                            DesktopPet.Services.Health.SystemHealthMetric.Memory => PetState.Confused,
                            DesktopPet.Services.Health.SystemHealthMetric.Disk => PetState.LookAround,
                            DesktopPet.Services.Health.SystemHealthMetric.Battery => PetState.Sleep,
                            _ => PetState.Idle
                        };
                        _engine?.TriggerReaction(reaction, line, cooldownSeconds: 30.0);
                    });
                }
            };
        }

        // Virtual toy lifecycle cleanup
        _toyService.AllToysCleared += () =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                foreach (var win in _activeToyWindows.ToList())
                {
                    try { win.Close(); } catch { }
                }
                _activeToyWindows.Clear();
            });
        };

        _reminderService = new ProductivityReminderService(_taskService, _focusTimer, _settings, Dispatcher);
        _reminderService.IsQuietOrPresenting = () => _hiddenForQuiet || (_settings.EnableQuietMode && _engine?.IsQuiet == true);
        _reminderService.TaskDeadlineReminder += (task, msg) =>
        {
            if (_settings.EnableQuietMode && (_hiddenForQuiet || _engine?.IsQuiet == true)) return;
            _engine?.TriggerReaction(PetState.Wave, msg, 3.0);
        };
        _reminderService.BreakReminder += msg =>
        {
            if (_settings.EnableQuietMode && (_hiddenForQuiet || _engine?.IsQuiet == true)) return;
            _engine?.TriggerReaction(PetState.Stretch, msg, 3.0);
        };

        _taskReminderService = new TaskReminderService(_taskService, _settings, Dispatcher);
        _taskReminderService.IsQuietOrPresenting = () => _hiddenForQuiet || (_settings.EnableQuietMode && _engine?.IsQuiet == true);
        _taskReminderService.ReminderDue += (task, isMissed) =>
        {
            Dispatcher.Invoke(() =>
            {
                var win = new ReminderNotificationWindow(task, _taskReminderService, isMissed, _settings.DefaultSnoozeMinutes);
                _taskReminderService.RegisterActiveNotification(task.Id, win);
                win.Show();

                if (!_settings.EnableQuietMode && !_hiddenForQuiet && _engine?.IsQuiet != true)
                {
                    _engine?.TriggerReaction(PetState.Wave, $"⏰ Reminder: {task.Title}", 3.0);
                }
            });
        };
        _taskReminderService.ConsolidatedRemindersDue += (count, tasks) =>
        {
            Dispatcher.Invoke(() =>
            {
                var win = new ReminderNotificationWindow(
                    tasks,
                    _taskReminderService,
                    onOpenTasks: () => OpenTasksWindow());
                win.Show();

                if (!_settings.EnableQuietMode && !_hiddenForQuiet && _engine?.IsQuiet != true)
                {
                    _engine?.TriggerReaction(PetState.Wave, $"⏰ {count} missed task reminders!", 3.0);
                }
            });
        };

        if (!string.IsNullOrEmpty(_settings.FocusActiveTaskId))
        {
            var activeTask = _taskService.Get(_settings.FocusActiveTaskId);
            if (activeTask != null)
            {
                _focusTimer.ActiveTaskTitle = activeTask.Title;
                _focusTimer.ActiveTaskProject = activeTask.Project;
            }
        }

        _petWindow.SetClickThrough(_settings.ClickThrough);
        _petWindow.Opacity = _settings.Opacity;
        _petWindow.NotesService = _notesService;
        _petWindow.TaskService = _taskService;
        _petWindow.Settings = _settings;
        _petWindow.ToyService = _toyService;
        _petWindow.OnSpawnToy = type => SpawnToyOnDesktop(type);
        _petWindow.OnClearToys = ClearAllToys;

        _petWindow.SetupContextMenu(
            onFocusToggle: OnFocusToggle,
            getFocusLabel: GetFocusLabel,
            onFocusCancel: OnFocusCancel,
            isFocusActive: () => _focusTimer.IsActive,
            onToggleFocusHud: ToggleFocusHud,
            onCommandPalette: ToggleCommandPalette,
            onProductivityHub: () => OpenProductivityHub(),
            onTasks: OpenTasksWindow,
            onQuickNote: OpenQuickNotes,
            onDeveloperEvents: OpenDeveloperEvents,
            onCompanionStudio: OpenCompanionStudio,
            onSettings: ShowSettings,
            onTogglePause: () => OnPauseToggled(_engine?.Paused != true),
            isPaused: () => _engine?.Paused == true,
            onHide: () => OnHideToggled(true),
            onToggleClickThrough: () => OnClickThroughToggled(!_settings.ClickThrough),
            isClickThrough: () => _settings.ClickThrough,
            onToggleReducedMotion: () => OnReducedMotionToggled(!_settings.ReducedMotion),
            isReducedMotion: () => _settings.ReducedMotion,
            onToggleQuietMode: () => OnQuietModeToggled(!_settings.EnableQuietMode),
            isQuietMode: () => _settings.EnableQuietMode,
            onResetPosition: OnResetPosition,
            onExit: QuitApp,
            onQuickCapture: () => OpenQuickCapture(),
            onDeveloperRadar: () => OpenProductivityHub("Radar"),
            onToggleDeveloperHud: ToggleDeveloperHud,
            onSpawnToy: type => SpawnToyOnDesktop(type),
            onClearToys: ClearAllToys,
            isDeveloperEventsEnabled: () => _settings.EnableDeveloperEvents,
            isDeveloperRadarEnabled: () => _settings.EnableDeveloperRadar,
            isDeveloperHudEnabled: () => _settings.EnableDeveloperHud || _settings.EnableDeveloperRadar);

        _tray = new TrayService(
            onFocusToggle: OnFocusToggle,
            getFocusLabel: GetFocusLabel,
            onFocusCancel: OnFocusCancel,
            isFocusActive: () => _focusTimer.IsActive,
            onCommandPalette: ToggleCommandPalette,
            onProductivityHub: () => OpenProductivityHub(),
            onTasks: OpenTasksWindow,
            onQuickNote: OpenQuickNotes,
            onDeveloperEvents: OpenDeveloperEvents,
            onCompanionStudio: OpenCompanionStudio,
            onSettings: ShowSettings,
            onPauseToggled: OnPauseToggled,
            onHideToggled: OnHideToggled,
            onClickThroughToggled: OnClickThroughToggled,
            onReducedMotionToggled: OnReducedMotionToggled,
            onQuietModeToggled: OnQuietModeToggled,
            onResetPosition: OnResetPosition,
            onQuit: QuitApp,
            onUpdate: RunUpdate,
            settings: _settings,
            onDeveloperRadar: () => OpenProductivityHub("Radar"),
            onToggleDeveloperHud: ToggleDeveloperHud,
            onToggleFocusHud: ToggleFocusHud,
            onQuickCapture: () => OpenQuickCapture(),
            onDoubleClick: SummonCompanionAndHub);

        _petWindow.PetInteracted += DismissOnboarding;

        if (!_settings.OnboardingDismissed)
        {
            var onboardingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            onboardingTimer.Tick += (_, _) =>
            {
                onboardingTimer.Stop();
                if (!_settings.OnboardingDismissed && _engine != null)
                {
                    _engine.ShowSpeech(
                        "👋 Hi! I'm Neku!\n• Click to interact\n• Right-click for actions\n• Drag to move me\n• Use tray icon to control\n(Click to dismiss)",
                        15.0);
                }
            };
            onboardingTimer.Start();
        }

        _developerEvents = new DeveloperEventService(_engine, _settings, Dispatcher, _agentActivityService);
        if (_settings.EnableDeveloperEvents || _settings.EnableAgentCompanion)
            _developerEvents.Start();

        _agentActivityService.EventReceived += OnAgentEventReceived;
        _agentActivityService.AllSessionsIdle += OnAgentAllSessionsIdle;

        _deskSense = new DeskSenseService(_settings, dispatcher: Dispatcher);
        _deskSense.TransitionOccurred += OnDeskSenseTransition;

        _repoMonitor = new RepositoryMonitorService(_settings);
        _radarService = new DeveloperRadarService(_settings, _deskSense, _repoMonitor, _developerEvents, _agentActivityService, _focusTimer);

        if (_settings.EnableDeskSense)
        {
            _deskSense.Start();
        }

        if (_settings.EnableDeveloperRadar)
        {
            _ = _repoMonitor.RefreshAllAsync();
        }

        if (_settings.EnableDeveloperHud)
        {
            ShowDeveloperHud();
        }

        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Global hotkey (registered on the pet window's HWND, which now exists).
        _hotkey = new HotkeyService();
        _hotkey.Attach(_petWindow.Handle);

        // Global exit hotkey (Ctrl + Alt + Q)
        bool exitRegistered = _hotkey.RegisterExit(() =>
        {
            DebugLog.Write("[Hotkey] Global exit shortcut (Ctrl+Alt+Q) triggered. Gracefully quitting.");
            QuitApp();
        });

        if (exitRegistered)
        {
            DebugLog.Write("[Hotkey] Global exit shortcut Ctrl+Alt+Q registered successfully.");
        }
        else
        {
            DebugLog.Write("[Hotkey] Global exit shortcut Ctrl+Alt+Q could not be registered (combo in use). Context menu/tray exit remains available.");
        }

        // Global summon hotkey (Ctrl + Alt + N)
        bool summonRegistered = _hotkey.RegisterSummon(() =>
        {
            Dispatcher.Invoke(SummonNeku);
        });

        if (summonRegistered)
        {
            DebugLog.Write("[Hotkey] Global summon shortcut Ctrl+Alt+N registered successfully.");
        }
        else
        {
            DebugLog.Write("[Hotkey] Global summon shortcut Ctrl+Alt+N could not be registered (combo in use).");
        }

        ApplyQuickCaptureHotkey();
        InitializePaletteCommands();
        ApplyPaletteHotkey();

        // Stretch timer — starts immediately if enabled.
        ResetStretchTimer();

        // Auto-update — quietly check GitHub, surface a tray prompt if a newer build exists.
        CheckForUpdates();

        if (e.Args.Contains("--test-interactive"))
        {
            var openTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            openTimer.Tick += (_, _) =>
            {
                openTimer.Stop();
                OnFocusToggle();
                ToggleFocusHud();
                OpenQuickNotes();

                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                closeTimer.Tick += (_, _) =>
                {
                    closeTimer.Stop();
                    _notesWindow?.Close();
                    if (_focusHudWindow?.IsVisible == true)
                        ToggleFocusHud();
                    OnFocusCancel();
                };
                closeTimer.Start();
            };
            openTimer.Start();
        }

        if (e.Args.Contains("--test-studio") || Environment.GetCommandLineArgs().Contains("--test-studio"))
        {
            DebugLog.Write("[Startup] --test-studio detected; starting automation sequence");
            string initialPet = _settings.ActivePet;
            var openTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            openTimer.Tick += (_, _) =>
            {
                openTimer.Stop();
                DebugLog.Write("[Startup] --test-studio opening Companion Studio");
                OpenCompanionStudio();

                var testTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                testTimer.Tick += (_, _) =>
                {
                    testTimer.Stop();
                    DebugLog.Write("[Startup] --test-studio switching companion to billu");
                    TrySwitchPet("billu");
                    DebugLog.Write("[Startup] --test-studio switching companion to cat");
                    TrySwitchPet("cat");
                    DebugLog.Write("[Startup] --test-studio switching companion to neku");
                    TrySwitchPet("neku");
                    if (!string.IsNullOrEmpty(initialPet) && !initialPet.Equals("neku", StringComparison.OrdinalIgnoreCase))
                    {
                        DebugLog.Write($"[Startup] --test-studio restoring original pet: {initialPet}");
                        TrySwitchPet(initialPet);
                    }
                    _studioWindow?.Close();

                    var exitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    exitTimer.Tick += (_, _) =>
                    {
                        exitTimer.Stop();
                        DebugLog.Write("[Startup] --test-studio automation completed successfully. Exiting.");
                        QuitApp();
                        Environment.Exit(0);
                    };
                    exitTimer.Start();
                };
                testTimer.Start();
            };
            openTimer.Start();
        }

        DebugLog.Write("[Startup] OnStartup initialization finished. Dispatcher running.");
    }

    private UpdateService Updater() => _updateService ??= new UpdateService(Http());

    private async void CheckForUpdates()
    {
        if (!_settings.EnableAutoUpdate) return;
        try
        {
            if (await Updater().IsUpdateAvailableAsync())
                _tray?.ShowUpdateAvailable();
        }
        catch { /* offline / rate-limited / no releases yet — ignore */ }
    }

    /// <summary>
    /// Apply an update. A git checkout hands off to update.bat (a developer wants their source
    /// pulled, not replaced by a release binary); a normal install downloads the published exe
    /// and lets a swap script put it in place once we have exited.
    /// </summary>
    private async void RunUpdate()
    {
        var updater = Updater();

        if (updater.IsSourceCheckout)
        {
            updater.RunUpdater();
            QuitApp();          // update.bat will rebuild and relaunch
            return;
        }

        _tray?.SetUpdateBusy(true);
        bool staged;
        try { staged = await updater.DownloadAndApplyAsync(); }
        catch { staged = false; }
        _tray?.SetUpdateBusy(false);

        if (staged)
        {
            QuitApp();          // the swap script is waiting for us to exit
        }
        else
        {
            MessageBox.Show(
                "Couldn't download the update. You can grab the latest build from the releases page instead.",
                "Neku", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (updater.LatestUrl != null) OpenUrl(updater.LatestUrl);
        }
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void ResetStretchTimer()
    {
        _stretchTimer?.Stop();
        _stretchTimer = null;

        if (_settings.StretchIntervalMinutes <= 0) return;

        _stretchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(_settings.StretchIntervalMinutes)
        };
        _stretchTimer.Tick += (_, _) => TriggerStretch();
        _stretchTimer.Start();
    }

    private void TriggerStretch()
    {
        if (_settings.EnableBreakReactions)
            _engine?.RequestStretch();

        // Show the cute notification
        var notification = new StretchNotification();
        notification.Show();
    }

    /// <summary>
    /// Load a pet pack by folder name, falling back to the default cat if the configured one
    /// has gone missing (a user could have deleted a custom pack they were using).
    /// Art comes from <see cref="AssetSource"/>, so this works whether the assets are embedded
    /// in the exe or sitting on disk beside it.
    /// </summary>
    private static SpriteAnimator LoadPet(string pet)
    {
        DebugLog.Write($"[LoadPet] Requested activePet: '{pet}'");
        foreach (string candidate in new[] { pet, "neku", "cat" })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                string dir = $"Assets/pets/{candidate}";
                string manifestRel = $"{dir}/manifest.json";
                if (!AssetSource.Exists(manifestRel))
                {
                    DebugLog.Write($"[LoadPet] Candidate '{candidate}' manifest not found at {manifestRel} (resolved: {AssetSource.ResolveSource(manifestRel)})");
                    continue;
                }

                string manifestPath = AssetSource.ResolveSource(manifestRel);
                var manifest = PetManifest.Parse(AssetSource.ReadAllText(manifestRel), dir);
                string sheetRel = $"{dir}/{manifest.Sheet}";
                string sheetPath = AssetSource.ResolveSource(sheetRel);

                DebugLog.Write($"[LoadPet] Successfully loaded pack: '{candidate}'");
                DebugLog.Write($"[LoadPet] Resolved manifest: {manifestPath}");
                DebugLog.Write($"[LoadPet] Resolved sheet: {sheetPath}");
                DebugLog.Write($"[LoadPet] Dimensions: {manifest.CellWidth}x{manifest.CellHeight}, Columns: {manifest.Columns}, Scale: {manifest.Scale}");

                var animator = new SpriteAnimator(manifest, AssetSource.ReadAllBytes(sheetRel));
                DebugLog.Write($"[LoadPet] Initial animation selected: '{animator.CurrentAnimationName}'");
                return animator;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[LoadPet] Failed to load candidate '{candidate}': {ex.Message}");
            }
        }

        throw new FileNotFoundException(
            $"No pet pack found for '{pet}', and the built-in cat is missing too.");
    }

    /// <summary>
    /// Switches the active desktop companion live at runtime. Returns true on success,
    /// or false if the requested pack cannot be loaded (preserving the current selection).
    /// </summary>
    public bool TrySwitchPet(string pet)
    {
        try
        {
            var (isValid, error) = AssetSource.ValidatePack(pet);
            if (!isValid)
            {
                DebugLog.Write($"[SwitchPet] Refusing to switch to invalid pack '{pet}': {error}");
                return false;
            }

            var animator = LoadPet(pet);
            _engine?.SwitchAnimator(animator);
            _settings.ActivePet = pet;
            _settingsService.Save();
            DebugLog.Write($"[SwitchPet] Live-switched active pet to '{pet}'");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[SwitchPet] Failed to switch to '{pet}': {ex.Message}");
            return false;
        }
    }

    private string GetFocusLabel()
    {
        return _focusTimer.State switch
        {
            FocusTimerState.Focusing => $"Focusing ({_focusTimer.FormattedRemaining}) - Click to Pause",
            FocusTimerState.FocusPaused => $"Focus Paused ({_focusTimer.FormattedRemaining}) - Click to Resume",
            FocusTimerState.Break => $"Break ({_focusTimer.FormattedRemaining}) - Click to Pause",
            FocusTimerState.BreakPaused => $"Break Paused ({_focusTimer.FormattedRemaining}) - Click to Resume",
            _ => $"Start Focus Session ({_settings.FocusDurationMinutes}m)"
        };
    }

    private void OnFocusToggle()
    {
        DismissOnboarding();
        switch (_focusTimer.State)
        {
            case FocusTimerState.Idle:
                string? taskId = _settings.FocusActiveTaskId;
                string? taskTitle = _focusTimer.ActiveTaskTitle;
                if (!string.IsNullOrEmpty(taskId) && string.IsNullOrEmpty(taskTitle))
                {
                    taskTitle = _taskService.Get(taskId)?.Title;
                }
                _focusTimer.StartFocus(_settings.FocusDurationMinutes, taskId, taskTitle);
                _engine?.Personality.OnPositiveEvent();
                if (_settings.EnableFocusReactions)
                    _engine?.TriggerReaction(PetState.Typing, _engine.Personality.GetFocusStartedLine(), 2.0);
                break;
            case FocusTimerState.Focusing:
                _focusTimer.Pause();
                break;
            case FocusTimerState.FocusPaused:
                _focusTimer.Resume();
                break;
            case FocusTimerState.Break:
                _focusTimer.Pause();
                break;
            case FocusTimerState.BreakPaused:
                _focusTimer.Resume();
                break;
        }
    }

    private void OnFocusCancel()
    {
        _focusTimer.Cancel();
        _settings.FocusActiveTaskId = null;
        _settingsService.Save();
        _engine?.ShowSpeech("Focus session canceled.", 2.5);
    }

    private void ToggleFocusHud()
    {
        DismissOnboarding();
        if (_focusHudWindow == null)
        {
            _focusHudWindow = new FocusHudWindow(_focusTimer, OnFocusToggle, OnFocusCancel);
            _focusHudWindow.Closed += (_, _) => _focusHudWindow = null;
        }

        if (_focusHudWindow.IsVisible)
            _focusHudWindow.Hide();
        else
        {
            _focusHudWindow.UpdateDisplay();
            _focusHudWindow.Show();
        }
    }

    public void ToggleDeveloperHud()
    {
        DismissOnboarding();
        if (_developerHudWindow != null && _developerHudWindow.IsVisible)
        {
            HideDeveloperHud();
        }
        else
        {
            ShowDeveloperHud();
        }
    }

    public void ShowDeveloperHud()
    {
        if (_radarService == null) return;

        if (_developerHudWindow == null)
        {
            _developerHudWindow = new DeveloperHudWindow(
                _settings,
                _radarService,
                onOpenRadar: () => OpenProductivityHub("Radar"),
                health: _healthService);
            _developerHudWindow.Closed += (_, _) => _developerHudWindow = null;
        }

        _developerHudWindow.Show();
        _settings.EnableDeveloperHud = true;
    }

    public void HideDeveloperHud()
    {
        if (_developerHudWindow != null)
        {
            _developerHudWindow.Hide();
        }
        _settings.EnableDeveloperHud = false;
    }

    private void OnDeskSenseTransition(DeskSenseTransition transition, DeskSenseContext context)
    {
        if (_engine == null) return;
        if (!_settings.EnableDeskSenseReactions) return;
        if (_settings.EnableQuietMode && (_hiddenForQuiet || _engine.IsQuiet)) return;

        string? speech = null;
        PetState state = PetState.LookAround;

        switch (transition)
        {
            case DeskSenseTransition.CodingStarted:
                if (!_settings.EnableCodingStartedReaction) return;
                state = PetState.Typing;
                speech = _engine.Personality.GetCodingStartedLine();
                break;

            case DeskSenseTransition.TerminalActive:
                if (!_settings.EnableTerminalActiveReaction) return;
                state = PetState.TypingFast;
                speech = _engine.Personality.GetTerminalActiveLine();
                break;

            case DeskSenseTransition.UserIdle:
                if (!_settings.EnableUserIdleReaction) return;
                state = PetState.Sleep;
                speech = _engine.Personality.GetUserIdleLine();
                break;

            case DeskSenseTransition.UserReturned:
                if (!_settings.EnableUserReturnedReaction) return;
                state = PetState.Wave;
                speech = _engine.Personality.GetUserReturnedLine();
                break;

            case DeskSenseTransition.PresentationStarted:
                if (!_settings.EnablePresentationStartedReaction) return;
                state = PetState.Sit;
                speech = _engine.Personality.GetPresentationStartedLine();
                break;

            default:
                return;
        }

        if (!string.IsNullOrWhiteSpace(speech))
        {
            _engine.TriggerDeskSenseReaction(state, speech);
        }
    }

    private void OnFocusCompleted()
    {
        if (!string.IsNullOrEmpty(_focusTimer.ActiveTaskId))
        {
            _taskService.RecordFocusCompleted(_focusTimer.ActiveTaskId, _focusTimer.FocusDurationMinutes);
            _focusTimer.ClearActiveTask();
            _settings.FocusActiveTaskId = null;
            _settingsService.Save();
        }

        _engine?.Personality.OnPositiveEvent();
        if (_settings.EnableFocusReactions && !_settings.EnableQuietMode && !_hiddenForQuiet && _engine?.IsQuiet != true)
        {
            double focusToday = _focusHistoryService.GetTodayFocusMinutes();
            if (focusToday >= _settings.DailyFocusGoalMinutes && (focusToday - _focusTimer.FocusDurationMinutes) < _settings.DailyFocusGoalMinutes)
            {
                _engine?.TriggerReaction(PetState.Celebrate, "🌟 Daily focus goal achieved! Fantastic work today! 🎉", 3.0);
            }
            else
            {
                _engine?.TriggerReaction(PetState.Celebrate, _engine.Personality.GetFocusCompletedLine(), 2.0);
            }

            if (!_settings.ReducedMotion)
            {
                _petWindow?.SpawnHearts();
            }
        }

        bool isLongBreak = (_focusTimer.LongBreakInterval > 0 && _focusTimer.CompletedSessionsInCycle % _focusTimer.LongBreakInterval == 0);
        int breakMins = isLongBreak ? _settings.LongBreakDurationMinutes : _settings.BreakDurationMinutes;
        string breakLabel = isLongBreak ? $"Start {breakMins}m Long Break 🌴" : $"Start {breakMins}m Break ☕";

        bool isQuietOrPresenting = _settings.EnableQuietMode || _hiddenForQuiet || _engine?.IsQuiet == true;
        if (_settings.EnableFocusCompletedReminders && !isQuietOrPresenting)
        {
            var notif = new FocusNotificationWindow(
                "Focus session complete! 🎉",
                $"Great work! Time for a {breakMins}m {(isLongBreak ? "long " : "")}break.",
                "🍅",
                breakLabel,
                () =>
                {
                    _focusTimer.StartBreak(isLongBreak, breakMins);
                    if (_settings.EnableBreakReactions && !_settings.EnableQuietMode && !_hiddenForQuiet && _engine?.IsQuiet != true)
                        _engine?.TriggerReaction(PetState.Stretch, "Break time! Stretch those paws and rest your eyes 🌸", 2.0);
                });
            notif.Show();
        }
    }

    private void OnBreakCompleted()
    {
        _engine?.Personality.OnPositiveEvent();
        if (_settings.EnableBreakReactions && !_settings.EnableQuietMode && !_hiddenForQuiet && _engine?.IsQuiet != true)
            _engine?.TriggerReaction(PetState.Wave, _engine.Personality.GetBreakCompletedLine(), 2.0);

        bool isQuietOrPresenting = _settings.EnableQuietMode || _hiddenForQuiet || _engine?.IsQuiet == true;
        if (_settings.EnableBreakCompletedReminders && !isQuietOrPresenting)
        {
            var notif = new FocusNotificationWindow(
                "Break finished! ✨",
                "Feeling refreshed? Ready for another session?",
                "☕",
                "Start Focus 🎯",
                () => _focusTimer.StartFocus(_settings.FocusDurationMinutes));
            notif.Show();
        }
    }

    public static void BringToForeground(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        EnsureWindowOnScreen(window);

        window.Show();
        window.Activate();
        bool wasTopmost = window.Topmost;
        window.Topmost = true;
        if (!wasTopmost) window.Topmost = false;
        window.Focus();
    }

    private static void EnsureWindowOnScreen(Window window)
    {
        double left = window.Left;
        double top = window.Top;
        double width = window.ActualWidth > 0 ? window.ActualWidth : (double.IsNaN(window.Width) ? 600 : window.Width);
        double height = window.ActualHeight > 0 ? window.ActualHeight : (double.IsNaN(window.Height) ? 400 : window.Height);

        double screenLeft = SystemParameters.VirtualScreenLeft;
        double screenTop = SystemParameters.VirtualScreenTop;
        double screenWidth = SystemParameters.VirtualScreenWidth;
        double screenHeight = SystemParameters.VirtualScreenHeight;

        if (left + width < screenLeft + 50 || left > screenLeft + screenWidth - 50 ||
            top + height < screenTop + 50 || top > screenTop + screenHeight - 50)
        {
            window.Left = Math.Max(screenLeft, (SystemParameters.PrimaryScreenWidth - width) / 2);
            window.Top = Math.Max(screenTop, (SystemParameters.PrimaryScreenHeight - height) / 2);
        }
    }

    private void SummonCompanionAndHub()
    {
        SummonNeku();
        OpenProductivityHub();
    }

    private void OpenQuickNotes()
    {
        DismissOnboarding();
        if (_notesWindow == null || !_notesWindow.IsLoaded)
        {
            _notesWindow = new QuickNotesWindow(_notesService);
            _notesWindow.Closed += (_, _) => _notesWindow = null;
            BringToForeground(_notesWindow);
        }
        else
        {
            BringToForeground(_notesWindow);
        }
    }

    private void OpenTasksWindow()
    {
        DismissOnboarding();
        if (_tasksWindow == null || !_tasksWindow.IsLoaded)
        {
            _tasksWindow = new TasksWindow(_taskService, _focusTimer, _settings);
            _tasksWindow.TaskFocusRequested += task =>
            {
                StartFocusForTask(task);
            };
            _tasksWindow.OpenHubRequested += () =>
            {
                OpenProductivityHub("Tasks");
            };
            _tasksWindow.Closed += (_, _) => _tasksWindow = null;
            BringToForeground(_tasksWindow);
        }
        else
        {
            BringToForeground(_tasksWindow);
        }
    }

    public void OpenQuickCapture(QuickCaptureMode initialMode = QuickCaptureMode.Task)
    {
        DismissOnboarding();
        if (_quickCaptureWindow == null || !_quickCaptureWindow.IsLoaded)
        {
            _quickCaptureWindow = new QuickCaptureWindow(_taskService, _notesService, initialMode);
            _quickCaptureWindow.Closed += (_, _) => _quickCaptureWindow = null;
            BringToForeground(_quickCaptureWindow);
        }
        else
        {
            _quickCaptureWindow.SetMode(initialMode);
            BringToForeground(_quickCaptureWindow);
        }
    }

    private void OpenProductivityHub(string? initialTab = null)
    {
        DismissOnboarding();
        if (_dashboardWindow == null || !_dashboardWindow.IsLoaded)
        {
            _dashboardWindow = new DashboardWindow(
                _taskService, _focusTimer, _focusHistoryService, _settings, initialTab,
                _agentActivityService, _claudeCodeService, _radarService, ToggleDeveloperHud,
                _healthService, _notesService);
            _dashboardWindow.TaskFocusRequested += task =>
            {
                StartFocusForTask(task);
            };
            _dashboardWindow.QuickNotesRequested += OpenQuickNotes;
            _dashboardWindow.Closed += (_, _) => _dashboardWindow = null;
            BringToForeground(_dashboardWindow);
        }
        else
        {
            BringToForeground(_dashboardWindow);
            if (!string.IsNullOrEmpty(initialTab))
            {
                _dashboardWindow.SelectTab(initialTab);
            }
        }
    }

    public void StartFocusForTask(TaskItem task)
    {
        if (_focusTimer.IsActive)
        {
            _focusTimer.Cancel();
        }
        int minutes = _settings.FocusDurationMinutes > 0 ? _settings.FocusDurationMinutes : 25;
        _focusTimer.StartFocus(minutes, task.Id, task.Title, task.Project);
        _settings.FocusActiveTaskId = task.Id;
        _settingsService.Save();

        if (_settings.EnableFocusReactions && !_settings.EnableQuietMode && !_hiddenForQuiet && _engine?.IsQuiet != true)
        {
            _engine?.Personality.OnPositiveEvent();
            _engine?.TriggerReaction(PetState.Typing, $"🎯 Focusing on:\n\"{task.Title}\"", 2.0);
        }
    }

    private void OnTaskCompleted(TaskItem task)
    {
        if (!_settings.EnableTaskReactions) return;
        if (_settings.EnableQuietMode || _hiddenForQuiet || _engine?.IsQuiet == true) return;

        int tasksToday = _taskService.Tasks.Count(t => t.IsCompleted && t.CompletedAt.HasValue && TaskItem.ToLocalDate(t.CompletedAt.Value) == DateTime.Today);
        _engine?.Personality.OnPositiveEvent();

        if (tasksToday == _settings.DailyTaskGoal)
        {
            _engine?.TriggerReaction(PetState.Celebrate, $"🎯 Daily task goal completed! ({tasksToday} tasks done) ✨", 3.0);
        }
        else
        {
            string line = _engine?.Personality.GetTaskCompletedLine(task.Title) ?? "Task completed! Great job! ✨";
            _engine?.TriggerReaction(PetState.Celebrate, line, 2.0);
        }

        if (!_settings.ReducedMotion)
        {
            _petWindow?.SpawnHearts();
        }
    }

    private void OnTaskDeleted(TaskItem task)
    {
        if (string.Equals(_focusTimer.ActiveTaskId, task.Id, StringComparison.Ordinal))
        {
            _focusTimer.ClearActiveTask();
            _settings.FocusActiveTaskId = null;
            _settingsService.Save();
        }
    }

    private void OpenDeveloperEvents()
    {
        DismissOnboarding();
        if (_devEventsWindow == null || !_devEventsWindow.IsLoaded)
        {
            _devEventsWindow = new DeveloperEventsWindow(_developerEvents, _engine!, _settings, _settingsService);
            _devEventsWindow.Closed += (_, _) => _devEventsWindow = null;
            _devEventsWindow.Show();
        }
        else
        {
            if (_devEventsWindow.WindowState == WindowState.Minimized)
                _devEventsWindow.WindowState = WindowState.Normal;
            _devEventsWindow.Activate();
        }
    }

    private void DismissOnboarding()
    {
        if (!_settings.OnboardingDismissed)
        {
            _settings.OnboardingDismissed = true;
            _settingsService.Save();
            _engine?.ClearSpeech();
        }
    }

    private void ShowSettings()

    {
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settings, _settingsService,
            onChanged: OnSettingsChanged,
            onCheckUpdate: () => Updater().DescribeCheckAsync(),
            onResetPosition: OnResetPosition,
            onTriggerDevEvent: (type, msg) => _developerEvents?.HandleEvent(type, msg));
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            ResetStretchTimer(); // interval may have changed
        };
        _settingsWindow.Show();
    }

    private CompanionStudioWindow? _studioWindow;

    public void OpenCompanionStudio()
    {
        if (_studioWindow is { IsLoaded: true })
        {
            if (_studioWindow.WindowState == WindowState.Minimized)
                _studioWindow.WindowState = WindowState.Normal;
            _studioWindow.Activate();
            return;
        }

        _studioWindow = new CompanionStudioWindow(_settings, _settingsService, OnSettingsChanged);
        _studioWindow.Closed += (_, _) => _studioWindow = null;
        _studioWindow.Show();
    }

    /// <summary>Access to the central CommandPaletteService instance for testing and dynamic commands.</summary>
    public CommandPaletteService PaletteService => _paletteService;

    /// <summary>Returns true if the Command Palette global hotkey is currently registered and active.</summary>
    public bool IsPaletteHotkeyRegistered =>
        _hotkey != null && (_hotkey.IsRegistered(HotkeyService.PaletteHotkeyId) || _hotkey.IsRegistered(HotkeyService.PaletteFallbackHotkeyId));

    /// <summary>Returns the display text for the currently active Command Palette hotkey, or null if inactive.</summary>
    public string? GetPaletteHotkeyLabel()
    {
        if (_hotkey == null || !_settings.EnableCommandPaletteHotkey) return null;
        if (_hotkey.IsRegistered(HotkeyService.PaletteHotkeyId))
        {
            var mods = (ModifierKeys)_settings.CommandPaletteHotkeyModifiers;
            var key = (Key)_settings.CommandPaletteHotkeyKey;
            return HotkeyService.FormatHotkey(mods, key);
        }
        if (_hotkey.IsRegistered(HotkeyService.PaletteFallbackHotkeyId))
        {
            return "Ctrl + Alt + Space";
        }
        return null;
    }

    /// <summary>Toggles the Command Palette overlay open or closed.</summary>
    public void ToggleCommandPalette()
    {
        if (_paletteWindow is { IsVisible: true })
        {
            _paletteWindow.Close();
            return;
        }

        OpenCommandPalette();
    }

    /// <summary>Opens and activates the Command Palette overlay window.</summary>
    public void OpenCommandPalette()
    {
        DismissOnboarding();
        if (_paletteWindow is { IsVisible: true })
        {
            _paletteWindow.Activate();
            return;
        }

        _paletteWindow = new CommandPaletteWindow(_paletteService);
        _paletteWindow.Closed += (_, _) => _paletteWindow = null;
        _paletteWindow.Show();
        _paletteWindow.Activate();
    }

    /// <summary>
    /// Brings the desktop companion to the visible work area, unhides it if user-hidden,
    /// or triggers a gentle acknowledgement reaction if already visible.
    /// </summary>
    public void SummonNeku()
    {
        if (_userHidden)
        {
            DebugLog.Write("[Summon] Unhiding user-hidden companion.");
            OnHideToggled(false);
        }

        if (_settings.ClickThrough)
        {
            OnClickThroughToggled(false);
        }

        if (_petWindow != null)
        {
            if (_petWindow.Visibility != Visibility.Visible)
            {
                _petWindow.Visibility = Visibility.Visible;
            }

            _petWindow.Topmost = true;
        }

        if (_engine != null && _engine.IsOffScreen())
        {
            DebugLog.Write("[Summon] Resetting pet position from off-screen.");
            _engine.ResetPosition();
            return;
        }

        if (_engine != null)
        {
            DebugLog.Write("[Summon] Companion visible; triggering acknowledgement wave.");
            _engine.TriggerReaction(PetState.Wave, "👋 Here I am!", cooldownSeconds: 1.0);
        }
    }

    public void SpawnToyOnDesktop(ToyType type)
    {
        double spawnX = 300;
        double spawnY = 300;

        if (_engine != null)
        {
            spawnX = Math.Max(50, _engine.X + 80);
            spawnY = Math.Max(50, _engine.Y + 20);
        }

        var toy = _toyService.SpawnToy(type, spawnX, spawnY);
        var win = new ToyWindow(toy, _toyService);
        _activeToyWindows.Add(win);
        win.Closed += (_, _) => _activeToyWindows.Remove(win);
        win.Show();

        string name = type switch
        {
            ToyType.Ball => "Tennis Ball 🎾",
            ToyType.Feather => "Feather Toy 🪶",
            ToyType.Cushion => "Comfy Cushion 🛋️",
            _ => "Toy"
        };
        _engine?.ShowSpeech($"Spawned {name}!", 2.0);
    }

    public void ClearAllToys()
    {
        _toyService.ClearAll();
        _engine?.ShowSpeech("Cleared all toys! 🧹", 2.0);
    }

    private void ApplyPaletteHotkey()
    {
        if (_hotkey == null) return;
        _hotkey.Unregister(HotkeyService.PaletteHotkeyId);
        _hotkey.Unregister(HotkeyService.PaletteFallbackHotkeyId);

        if (_settings.EnableCommandPaletteHotkey)
        {
            var mods = (ModifierKeys)_settings.CommandPaletteHotkeyModifiers;
            var key = (Key)_settings.CommandPaletteHotkeyKey;
            bool registered = _hotkey.RegisterPalette(mods, key, () =>
            {
                Dispatcher.Invoke(ToggleCommandPalette);
            });

            if (registered)
            {
                DebugLog.Write($"[Hotkey] Command Palette hotkey ({mods}+{key}) registered successfully.");
            }
            else
            {
                DebugLog.Write($"[Hotkey] Command Palette hotkey ({mods}+{key}) could not be registered (combo in use). Attempting fallback Ctrl+Alt+Space...");
                bool fallback = _hotkey.Register(HotkeyService.PaletteFallbackHotkeyId, ModifierKeys.Control | ModifierKeys.Alt, Key.Space, () =>
                {
                    Dispatcher.Invoke(ToggleCommandPalette);
                });

                if (fallback)
                {
                    DebugLog.Write("[Hotkey] Command Palette fallback hotkey (Ctrl+Alt+Space) registered successfully.");
                }
                else
                {
                    DebugLog.Write("[Hotkey] Command Palette hotkeys unavailable (all combos in use). Context menu/tray entry points remain available.");
                }
            }
        }

        UpdatePaletteShortcutMenuLabels();
    }

    private void UpdatePaletteShortcutMenuLabels()
    {
        string? label = GetPaletteHotkeyLabel();
        _tray?.UpdatePaletteShortcut(label);
        _petWindow?.UpdatePaletteShortcut(label);
    }

    private void ApplyQuickCaptureHotkey()
    {
        if (_hotkey == null) return;
        _hotkey.UnregisterQuickCapture();

        if (_settings.EnableQuickCaptureHotkey)
        {
            var mods = (ModifierKeys)_settings.QuickCaptureHotkeyModifiers;
            var key = (Key)_settings.QuickCaptureHotkeyKey;
            bool registered = _hotkey.RegisterQuickCapture(mods, key, () => Dispatcher.Invoke(() => OpenQuickCapture()));

            if (registered)
            {
                DebugLog.Write($"[Hotkey] Global Quick Capture shortcut {HotkeyService.FormatHotkey(mods, key)} registered successfully.");
            }
            else
            {
                DebugLog.Write($"[Hotkey] Global Quick Capture shortcut {HotkeyService.FormatHotkey(mods, key)} could not be registered (combo in use).");
            }
        }
    }

    private void InitializePaletteCommands()
    {
        _paletteService.Clear();

        // 1. Productivity Commands
        _paletteService.Register(new PaletteCommand(
            id: "hub.open",
            title: "Open Productivity Hub",
            description: "View productivity dashboard, tasks, focus sessions, and analytics",
            category: "Productivity",
            action: () => OpenProductivityHub(),
            icon: "🐾",
            keywords: new[] { "hub", "productivity", "dashboard", "overview", "stats" }));

        _paletteService.Register(new PaletteCommand(
            id: "tasks.open",
            title: "Open Tasks",
            description: "Manage to-do list, due dates, priorities, and subtasks",
            category: "Productivity",
            action: () => OpenTasksWindow(),
            icon: "📋",
            keywords: new[] { "tasks", "todo", "list", "manage", "checklist" }));

        _paletteService.Register(new PaletteCommand(
            id: "tasks.create",
            title: "Create Task",
            description: "Quickly create a new task in your task list",
            category: "Productivity",
            action: () =>
            {
                OpenTasksWindow();
                _tasksWindow?.CreateNewTask();
            },
            icon: "➕",
            shortcutHint: "Ctrl+N",
            keywords: new[] { "new task", "add task", "create task", "todo" }));

        _paletteService.Register(new PaletteCommand(
            id: "quickcapture.open",
            title: "Quick Capture",
            description: "Capture a task or quick note in seconds (Ctrl+Alt+C)",
            category: "Productivity",
            action: () => OpenQuickCapture(),
            icon: "⚡",
            shortcutHint: "Ctrl+Alt+C",
            keywords: new[] { "quick capture", "capture", "quick add", "task", "note", "thought" }));

        _paletteService.Register(new PaletteCommand(
            id: "focus.start",
            title: "Start Focus Session",
            description: $"Begin a {_settings.FocusDurationMinutes}-minute Pomodoro focus block",
            category: "Productivity",
            action: () =>
            {
                if (!_focusTimer.IsActive)
                {
                    OnFocusToggle();
                }
                else if (_focusTimer.IsPaused)
                {
                    _focusTimer.Resume();
                }
            },
            icon: "🎯",
            keywords: new[] { "focus", "pomodoro", "timer", "work", "start" }));

        _paletteService.Register(new PaletteCommand(
            id: "focus.pause_resume",
            title: "Pause or Resume Focus",
            description: "Pause an active focus timer or resume a paused session",
            category: "Productivity",
            action: () => OnFocusToggle(),
            icon: "⏯️",
            keywords: new[] { "pause", "resume", "hold", "continue", "focus" }));

        _paletteService.Register(new PaletteCommand(
            id: "focus.cancel",
            title: "Cancel Focus Session",
            description: "Cancel the currently running focus or break session",
            category: "Productivity",
            action: () => OnFocusCancel(),
            icon: "⏹️",
            keywords: new[] { "cancel", "stop", "abort", "focus", "end" }));

        _paletteService.Register(new PaletteCommand(
            id: "focus.short_break",
            title: "Start Short Break",
            description: $"Take a {_settings.BreakDurationMinutes}-minute quick rest",
            category: "Productivity",
            action: () => _focusTimer.StartBreak(false, _settings.BreakDurationMinutes),
            icon: "☕",
            keywords: new[] { "short break", "break", "rest", "coffee", "pause" }));

        _paletteService.Register(new PaletteCommand(
            id: "focus.long_break",
            title: "Start Long Break",
            description: $"Take a {_settings.LongBreakDurationMinutes}-minute extended rest",
            category: "Productivity",
            action: () => _focusTimer.StartBreak(true, _settings.LongBreakDurationMinutes),
            icon: "🌴",
            keywords: new[] { "long break", "break", "rest", "relax", "extended" }));

        _paletteService.Register(new PaletteCommand(
            id: "hub.history",
            title: "Open Focus History",
            description: "Review past focus sessions, notes, and completion records",
            category: "Productivity",
            action: () => OpenProductivityHub("History"),
            icon: "📜",
            keywords: new[] { "history", "sessions", "past", "log", "records" }));

        _paletteService.Register(new PaletteCommand(
            id: "hub.insights",
            title: "Open Insights & Analytics",
            description: "View daily and weekly trends, heatmap, and planned vs actual time",
            category: "Productivity",
            action: () => OpenProductivityHub("Insights"),
            icon: "📊",
            keywords: new[] { "insights", "analytics", "charts", "trends", "heatmap", "metrics" }));

        _paletteService.Register(new PaletteCommand(
            id: "hub.goals",
            title: "Open Productivity Goals",
            description: "Check daily/weekly focus and task targets, streaks, and achievements",
            category: "Productivity",
            action: () => OpenProductivityHub("Goals"),
            icon: "🏆",
            keywords: new[] { "goals", "targets", "streaks", "daily goal", "weekly goal" }));

        // 2. Notes Commands
        _paletteService.Register(new PaletteCommand(
            id: "notes.open",
            title: "Open Quick Notes",
            description: "Open your scratchpad for thoughts, snippets, and ideas",
            category: "Notes",
            action: () => OpenQuickNotes(),
            icon: "📝",
            keywords: new[] { "notes", "scratchpad", "quick notes", "memo" }));

        _paletteService.Register(new PaletteCommand(
            id: "notes.create",
            title: "Create New Note",
            description: "Create a fresh note in Quick Notes and start writing immediately",
            category: "Notes",
            action: () =>
            {
                OpenQuickNotes();
                _notesWindow?.CreateNewNote();
            },
            icon: "✏️",
            keywords: new[] { "new note", "add note", "create note", "write" }));

        // 3. Companions Commands
        _paletteService.Register(new PaletteCommand(
            id: "companions.studio",
            title: "Open Companion Studio",
            description: "Preview companions, test animations, and configure personality",
            category: "Companions",
            action: () => OpenCompanionStudio(),
            icon: "🎨",
            keywords: new[] { "studio", "companion studio", "skins", "characters", "preview" }));

        // Dynamic registration for all installed public companions
        var packs = AssetSource.DiscoverPacks();
        foreach (var pack in packs)
        {
            if (!pack.IsValid) continue;
            if (pack.Id.Equals("neku-personal-local", StringComparison.OrdinalIgnoreCase)) continue;

            string packId = pack.Id;
            string packName = pack.DisplayName;
            _paletteService.Register(new PaletteCommand(
                id: $"companion.switch.{packId}",
                title: $"Switch Companion: {packName}",
                description: string.IsNullOrWhiteSpace(pack.Description)
                    ? $"Switch active desktop companion to {packName}"
                    : pack.Description,
                category: "Companions",
                action: () =>
                {
                    if (TrySwitchPet(packId))
                    {
                        _engine?.ShowSpeech($"Switched to {packName}! ✨", 2.0);
                    }
                },
                icon: "🐱",
                keywords: new[] { "switch", "companion", "pet", packId, packName, "character" }));
        }

        // 4. Application Commands
        _paletteService.Register(new PaletteCommand(
            id: "app.summon",
            title: "Show or Summon Neku",
            description: "Unhide Neku or bring the companion into the visible work area",
            category: "Application",
            action: () => SummonNeku(),
            icon: "👋",
            keywords: new[] { "summon", "show", "unhide", "find", "bring back", "neku" }));

        _paletteService.Register(new PaletteCommand(
            id: "app.hide",
            title: "Hide Neku",
            description: "Temporarily hide the desktop companion without exiting the app",
            category: "Application",
            action: () => OnHideToggled(true),
            icon: "👁️",
            keywords: new[] { "hide", "conceal", "vanish", "invisible" }));

        _paletteService.Register(new PaletteCommand(
            id: "app.settings",
            title: "Open Settings",
            description: "Configure pet scale, speed, companions, reminders, and behavior",
            category: "Application",
            action: () => ShowSettings(),
            icon: "⚙️",
            keywords: new[] { "settings", "preferences", "config", "options" }));

        _paletteService.Register(new PaletteCommand(
            id: "app.quiet_mode",
            title: "Toggle Quiet Mode",
            description: "Toggle quiet mode for presentations, screen sharing, and focused work",
            category: "Application",
            action: () => OnQuietModeToggled(!_settings.EnableQuietMode),
            icon: "🤫",
            keywords: new[] { "quiet mode", "do not disturb", "silence", "dnd", "presentation" }));

        // 5. Coding Agent Commands
        _paletteService.Register(new PaletteCommand(
            id: "agent.open_activity",
            title: "Open Agent Activity",
            description: "View active AI coding agent sessions, events, and status",
            category: "Agent",
            action: () => OpenProductivityHub("Agent"),
            icon: "🤖",
            keywords: new[] { "agent", "coding agent", "claude", "activity", "sessions", "hooks" }));

        _paletteService.Register(new PaletteCommand(
            id: "agent.show_active",
            title: "Show Active Agent Sessions",
            description: "Filter and view currently working coding agents",
            category: "Agent",
            action: () =>
            {
                OpenProductivityHub("Agent");
                _dashboardWindow?.SelectTab("Agent");
            },
            icon: "⚡",
            keywords: new[] { "active agent", "running agent", "working", "claude active" }));

        _paletteService.Register(new PaletteCommand(
            id: "agent.clear_history",
            title: "Clear Completed Agent History",
            description: "Remove completed, failed, and idle agent session logs",
            category: "Agent",
            action: () =>
            {
                int count = _agentActivityService.ClearCompletedHistory();
                _engine?.ShowSpeech($"Cleared {count} completed agent sessions! 🧹", 2.5);
            },
            icon: "🧹",
            keywords: new[] { "clear agent", "clean agent history", "purge sessions" }));

        _paletteService.Register(new PaletteCommand(
            id: "agent.configure_provider",
            title: "Configure Claude Code Integration",
            description: "Inspect setup, diff, and toggle hooks for Claude Code",
            category: "Agent",
            action: () => OpenProductivityHub("Agent"),
            icon: "⚙️",
            keywords: new[] { "claude hooks", "configure agent", "hook setup", "diff" }));

        // 6. DeskSense & Developer Radar Commands
        _paletteService.Register(new PaletteCommand(
            id: "desksense.toggle",
            title: "Toggle DeskSense",
            description: "Enable or disable DeskSense context awareness and companion reactions",
            category: "Radar",
            action: () =>
            {
                _settings.EnableDeskSense = !_settings.EnableDeskSense;
                if (_settings.EnableDeskSense) _deskSense?.Start();
                else _deskSense?.Stop();
                _settingsService.Save();
                _engine?.ShowSpeech(_settings.EnableDeskSense ? "DeskSense active! 🧠" : "DeskSense paused 💤", 2.0);
            },
            icon: "🧠",
            keywords: new[] { "desksense", "context", "detection", "coding awareness" }));

        _paletteService.Register(new PaletteCommand(
            id: "radar.open",
            title: "Open Developer Radar",
            description: "View workspace status, monitored repositories, build outcomes, and agent count",
            category: "Radar",
            action: () => OpenProductivityHub("Radar"),
            icon: "📡",
            keywords: new[] { "radar", "developer radar", "git", "builds", "workspace" }));

        _paletteService.Register(new PaletteCommand(
            id: "radar.hud.toggle",
            title: "Toggle Developer HUD",
            description: "Show or hide the compact floating Developer HUD pill",
            category: "Radar",
            action: ToggleDeveloperHud,
            icon: "🪟",
            keywords: new[] { "hud", "developer hud", "floating hud", "pill", "status overlay" }));

        _paletteService.Register(new PaletteCommand(
            id: "radar.repo.add",
            title: "Add Repository to Developer Radar",
            description: "Pick a local Git repository folder to monitor",
            category: "Radar",
            action: () =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Git Repository Folder",
                    Multiselect = false
                };
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
                {
                    _repoMonitor?.AddRepository(dialog.FolderName);
                    _engine?.ShowSpeech($"Added repo: {Path.GetFileName(dialog.FolderName)} 📁", 2.5);
                }
            },
            icon: "➕",
            keywords: new[] { "add repo", "track git", "monitor repository" }));

        _paletteService.Register(new PaletteCommand(
            id: "radar.repo.refresh",
            title: "Refresh Developer Radar Repositories",
            description: "Force immediate status recheck on all monitored Git repositories",
            category: "Radar",
            action: () =>
            {
                _ = _repoMonitor?.RefreshAllAsync();
                _engine?.ShowSpeech("Repositories refreshed! 🔄", 2.0);
            },
            icon: "🔄",
            keywords: new[] { "refresh repos", "check git", "recheck branch" }));

        _paletteService.Register(new PaletteCommand(
            id: "radar.repo.pause",
            title: "Pause Repository Monitoring",
            description: "Temporarily pause filesystem watching and periodic git checks",
            category: "Radar",
            action: () =>
            {
                if (_repoMonitor != null)
                {
                    _repoMonitor.IsPaused = true;
                    _settings.PauseRepositoryMonitoring = true;
                    _settingsService.Save();
                    _engine?.ShowSpeech("Repo monitoring paused ⏸️", 2.0);
                }
            },
            icon: "⏸️",
            keywords: new[] { "pause radar", "pause git polling" }));

        _paletteService.Register(new PaletteCommand(
            id: "radar.repo.resume",
            title: "Resume Repository Monitoring",
            description: "Resume filesystem watching and periodic git checks",
            category: "Radar",
            action: () =>
            {
                if (_repoMonitor != null)
                {
                    _repoMonitor.IsPaused = false;
                    _settings.PauseRepositoryMonitoring = false;
                    _settingsService.Save();
                    _engine?.ShowSpeech("Repo monitoring resumed ▶️", 2.0);
                }
            },
            icon: "▶️",
            keywords: new[] { "resume radar", "resume git polling" }));

        _paletteService.Register(new PaletteCommand(
            id: "radar.repo.open",
            title: "Open Selected Repository in Explorer",
            description: "Reveal the currently monitored repository folder in Windows Explorer",
            category: "Radar",
            action: () =>
            {
                if (_radarService?.OpenInExplorer() != true)
                {
                    _engine?.ShowSpeech("No repository selected or path missing 📁", 2.0);
                }
            },
            icon: "📂",
            keywords: new[] { "open repo", "explore repository", "reveal folder" }));

        _paletteService.Register(new PaletteCommand(
            id: "radar.repo.copy",
            title: "Copy Selected Repository Path",
            description: "Copy monitored repository path to Windows Clipboard",
            category: "Radar",
            action: () =>
            {
                if (_radarService?.CopyPathToClipboard() == true)
                {
                    _engine?.ShowSpeech("Repository path copied! 📋", 2.0);
                }
                else
                {
                    _engine?.ShowSpeech("No repository selected to copy 📋", 2.0);
                }
            },
            icon: "📋",
            keywords: new[] { "copy repo path", "copy folder path" }));

        // 7. System Health Commands
        _paletteService.Register(new PaletteCommand(
            id: "hub.health",
            title: "Open System Health",
            description: "View CPU, memory, disk, and process status in the Productivity Hub",
            category: "Health",
            action: () =>
            {
                OpenProductivityHub("Health");
                _dashboardWindow?.SelectTab("Health");
            },
            icon: "💻",
            keywords: new[] { "health", "system health", "cpu", "ram", "memory", "specs", "disk", "hardware" }));

        _paletteService.Register(new PaletteCommand(
            id: "health.hud.toggle",
            title: "Toggle System Health HUD",
            description: "Toggle system health stats in the floating Developer HUD",
            category: "Health",
            action: () =>
            {
                _settings.EnableHealthHud = !_settings.EnableHealthHud;
                _settingsService.Save();
                if (_settings.EnableHealthHud && !_settings.EnableDeveloperHud)
                {
                    _settings.EnableDeveloperHud = true;
                    ShowDeveloperHud();
                }
                else if (_developerHudWindow != null)
                {
                    _developerHudWindow.RefreshDisplay();
                }
                _engine?.ShowSpeech(_settings.EnableHealthHud ? "Health HUD enabled! 💻" : "Health HUD hidden", 2.0);
            },
            icon: "📊",
            keywords: new[] { "health hud", "cpu hud", "ram hud", "hardware hud" }));

        // 8. Desktop Toys & Dropzone Commands
        _paletteService.Register(new PaletteCommand(
            id: "pet.toy.ball",
            title: "Spawn Toy: Ball",
            description: "Spawn a bouncy tennis ball for your companion on the desktop",
            category: "Toys",
            action: () => SpawnToyOnDesktop(ToyType.Ball),
            icon: "🎾",
            keywords: new[] { "toy", "ball", "tennis ball", "fetch", "play" }));

        _paletteService.Register(new PaletteCommand(
            id: "pet.toy.feather",
            title: "Spawn Toy: Feather",
            description: "Spawn a fluttering feather toy for your companion on the desktop",
            category: "Toys",
            action: () => SpawnToyOnDesktop(ToyType.Feather),
            icon: "🪶",
            keywords: new[] { "toy", "feather", "wand", "chase", "play" }));

        _paletteService.Register(new PaletteCommand(
            id: "pet.toy.cushion",
            title: "Spawn Toy: Cushion",
            description: "Spawn a soft cushion for your companion to lounge on",
            category: "Toys",
            action: () => SpawnToyOnDesktop(ToyType.Cushion),
            icon: "🛋️",
            keywords: new[] { "toy", "cushion", "pillow", "nap", "loaf", "rest" }));

        _paletteService.Register(new PaletteCommand(
            id: "pet.toy.clear",
            title: "Clear All Toys",
            description: "Remove all virtual toys from the desktop",
            category: "Toys",
            action: ClearAllToys,
            icon: "🧹",
            keywords: new[] { "clear toys", "remove toys", "clean toys", "pack away" }));

        _paletteService.Register(new PaletteCommand(
            id: "dropzone.toggle",
            title: "Toggle Neku Dropzone",
            description: "Enable or disable dragging files, links, and text onto your pet",
            category: "Companions",
            action: () =>
            {
                _settings.EnableDropzone = !_settings.EnableDropzone;
                _settingsService.Save();
                if (_petWindow != null) _petWindow.AllowDrop = _settings.EnableDropzone;
                _engine?.ShowSpeech(_settings.EnableDropzone ? "Dropzone active! Drop items on me! 📥" : "Dropzone disabled 🚫", 2.0);
            },
            icon: "📥",
            keywords: new[] { "dropzone", "drag and drop", "file drop", "quick capture" }));
    }

    private void OnAgentEventReceived(AgentEvent evt)
    {
        if (!_settings.EnableAgentCompanion || !_settings.EnableAgentReactions || _engine == null || _engine.IsQuiet)
            return;

        switch (evt.EventType)
        {
            case AgentEventType.SessionStarted when _settings.EnableAgentStartedReaction:
                _engine.TriggerAgentReaction(PetState.Wave, _engine.Personality.GetAgentStartedLine());
                break;

            case AgentEventType.WaitingForInput when _settings.EnableAgentWaitingReaction:
                _engine.TriggerAgentReaction(PetState.Surprised, _engine.Personality.GetAgentWaitingLine());
                break;

            case AgentEventType.TaskCompleted when _settings.EnableAgentCompletedReaction:
                _engine.TriggerAgentReaction(PetState.Celebrate, _engine.Personality.GetAgentCompletedLine());
                break;

            case AgentEventType.TaskFailed when _settings.EnableAgentFailedReaction:
                _engine.TriggerAgentReaction(PetState.Annoyed, _engine.Personality.GetAgentFailedLine());
                break;
        }
    }

    private void OnAgentAllSessionsIdle()
    {
        if (!_settings.EnableAgentCompanion || !_settings.EnableAgentReactions || !_settings.EnableAgentIdleReaction || _engine == null || _engine.IsQuiet)
            return;

        _engine.TriggerAgentReaction(PetState.Loaf, _engine.Personality.GetAgentIdleLine());
    }

    private void OnPauseToggled(bool paused)
    {
        if (_engine != null) _engine.Paused = paused;
        _tray?.SetPaused(paused);

        // Let the app actually go idle while paused: with the render handler detached, WPF stops
        // compositing a frame 60 times a second for a cat that isn't moving. Clear the
        // decorations first — once ticking stops, nothing else would take them down.
        if (paused)
        {
            _engine?.ClearDecorations();
            _petWindow?.SetRenderLoopEnabled(false);
        }
        else
        {
            // Coming back from pause must not override quiet-mode hiding.
            _hiddenForQuiet = false;
            UpdateQuietHiding();
            if (!_hiddenForQuiet && !_userHidden) _petWindow?.SetRenderLoopEnabled(true);
        }
    }

    private void OnHideToggled(bool hidden)
    {
        _userHidden = hidden;
        _tray?.SetHidden(hidden);
        if (_petWindow != null && _engine != null)
        {
            if (hidden)
            {
                _engine.ClearDecorations();
                _petWindow.SetPetVisible(false);
                _petWindow.SetRenderLoopEnabled(false);
            }
            else
            {
                _petWindow.SetPetVisible(true);
                if (!_engine.Paused && !_hiddenForQuiet)
                    _petWindow.SetRenderLoopEnabled(true);
            }
        }
    }

    private void OnClickThroughToggled(bool enabled)
    {
        _settings.ClickThrough = enabled;
        _petWindow?.SetClickThrough(enabled);
        _tray?.SetClickThrough(enabled);
        _settingsService.Save();
    }

    private void OnReducedMotionToggled(bool enabled)
    {
        _settings.ReducedMotion = enabled;
        _tray?.SetReducedMotion(enabled);
        _engine?.HandleReducedMotionToggled(enabled);
        _settingsService.Save();
    }

    private void OnQuietModeToggled(bool enabled)
    {
        _settings.EnableQuietMode = enabled;
        _tray?.SetQuietMode(enabled);
        UpdateQuietHiding();
        _settingsService.Save();
    }

    private void OnResetPosition()
    {
        _engine?.ResetPosition();
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
        {
            DebugLog.Write("System suspending — pausing simulation and detaching render loop.");
            _petWindow?.SetRenderLoopEnabled(false);
            _engine?.ClearDecorations();
            _developerEvents?.Stop();
        }
        else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            DebugLog.Write("System resumed — restoring simulation.");
            if (_engine != null && !_engine.Paused && !_hiddenForQuiet && !_userHidden)
            {
                _petWindow?.SetRenderLoopEnabled(true);
            }
            if (_settings.EnableDeveloperEvents || _settings.EnableAgentCompanion)
            {
                _developerEvents?.Start();
            }
        }
    }

    // ── AI companion ────────────────────────────────────────────────────────────
    // Off-switch guarantee: nothing here builds an HttpClient or makes a network call
    // unless EnableAiCompanion is true. Every entry point re-checks the flag.

    private void OnSettingsChanged()
    {
        _engine?.ApplySize(_settings.SizeScale);
        if (_petWindow != null)
        {
            _petWindow.TargetFps = _settings.TargetFps;
            _petWindow.SetClickThrough(_settings.ClickThrough);
            _petWindow.Opacity = _settings.Opacity;
            _petWindow.AllowDrop = _settings.EnableDropzone;
            _petWindow.Settings = _settings;
        }
        _tray?.SetClickThrough(_settings.ClickThrough);
        _tray?.SetReducedMotion(_settings.ReducedMotion);
        _tray?.SetQuietMode(_settings.EnableQuietMode);
        _tray?.RefreshDeveloperMenuVisibility();
        _petWindow?.RefreshDeveloperMenuVisibility();

        if (_settings.EnableDeveloperEvents || _settings.EnableAgentCompanion)
            _developerEvents?.Start();
        else
            _developerEvents?.Stop();

        if (_settings.EnableDeskSense)
            _deskSense?.Start();
        else
            _deskSense?.Stop();

        if (_settings.EnableSystemHealthMonitoring)
        {
            if (_healthService == null)
            {
                _healthService = new DesktopPet.Services.Health.SystemHealthService(_settings);
                _healthService.AlertTriggered += alert =>
                {
                    if (_settings.EnableHealthCompanionAlerts)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (_settings.EnableQuietMode || _hiddenForQuiet || _engine?.IsQuiet == true) return;
                            string line = _engine?.Personality.GetHealthAlertLine(alert.Metric) ?? alert.Message;
                            PetState reaction = alert.Metric switch
                            {
                                DesktopPet.Services.Health.SystemHealthMetric.Cpu => PetState.Surprised,
                                DesktopPet.Services.Health.SystemHealthMetric.Memory => PetState.Confused,
                                DesktopPet.Services.Health.SystemHealthMetric.Disk => PetState.LookAround,
                                DesktopPet.Services.Health.SystemHealthMetric.Battery => PetState.Sleep,
                                _ => PetState.Idle
                            };
                            _engine?.TriggerReaction(reaction, line, cooldownSeconds: 30.0);
                        });
                    }
                };
            }
        }
        else
        {
            _healthService?.Dispose();
            _healthService = null;
        }

        if (_settings.EnableDeveloperHud)
            ShowDeveloperHud();
        else
            HideDeveloperHud();

        if (_developerHudWindow != null)
        {
            _developerHudWindow.RefreshDisplay();
        }

        if (_settings.DisableOptionalMonitoring)
        {
            _hookWatchdog?.Dispose();
            _hookWatchdog = null;
            _keyboard?.Dispose();
            _keyboard = null;
            _mouse?.Dispose();
            _mouse = null;
        }
        else
        {
            _keyboard ??= new KeyboardMonitor();
            _mouse ??= new MouseMonitor();
            _hookWatchdog ??= new HookWatchdog(_keyboard, _mouse, _system);
        }

        UpdateQuietHiding();
        ApplyPaletteHotkey();
        ApplyQuickCaptureHotkey();
        InitializePaletteCommands();
    }

    /// <summary>
    /// Hide the cat outright while the user is presenting, if they asked for that.
    ///
    /// Hiding detaches the render loop rather than just collapsing the image, which is what
    /// makes it free: being subscribed to WPF's per-frame Rendering event costs ~2.9% of a core
    /// even with a handler that does nothing, so a "hidden" pet that kept ticking would still
    /// be taxing the machine during exactly the presentation or game it was told to stay out of.
    /// </summary>
    private void UpdateQuietHiding()
    {
        if (_petWindow == null || _engine == null) return;
        if (_engine.Paused) return;      // pause already owns the render loop

        _system?.Poll();
        bool shouldHide = _settings.EnableQuietMode
                          && _settings.HideWhenPresenting
                          && _system?.ShouldNotDisturb == true;

        if (shouldHide == _hiddenForQuiet) return;
        _hiddenForQuiet = shouldHide;

        DebugLog.Write(shouldHide
            ? $"Hiding the cat ({_system?.Quiet}) and stopping the render loop."
            : "Showing the cat again and restarting the render loop.");

        if (shouldHide) _engine.ClearDecorations();
        _petWindow.SetPetVisible(!shouldHide);
        _petWindow.SetRenderLoopEnabled(!shouldHide);
    }

    /// <summary>Write the cat's position, mood, and focus timer into settings and save.</summary>
    private void SavePetState()
    {
        _focusTimer?.SaveState(_settings);
        if (_engine == null || !_settings.RememberPetState) return;

        double oldX = _settings.LastPetX;
        double oldY = _settings.LastPetY;
        double oldEnergy = _settings.LastEnergy;
        double oldHunger = _settings.LastHunger;
        _engine.CaptureStateInto(_settings);

        if (Math.Abs(oldX - _settings.LastPetX) > 1.0 ||
            Math.Abs(oldY - _settings.LastPetY) > 1.0 ||
            Math.Abs(oldEnergy - _settings.LastEnergy) > 0.05 ||
            Math.Abs(oldHunger - _settings.LastHunger) > 0.05)
        {
            _settingsService.Save();
        }
    }


    /// <summary>
    /// The one HttpClient in the app, used exclusively for GitHub release updates.
    /// </summary>
    private HttpClient Http() => _http ??= CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Neku/{AppVersion.Current} (+https://github.com/kunal-gswm/Neku)");
        return http;
    }

    private void QuitApp()
    {
        DebugLog.Write($"[Lifecycle] QuitApp called. Stack: {Environment.StackTrace}");
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SavePetState();
        try { _notesService?.Save(); } catch { }
        _stateSaveTimer?.Stop();
        _quietWatchTimer?.Stop();
        _developerEvents?.Dispose();
        _ipcServer?.Dispose();
        _commandIpcServer?.Dispose();
        _hotkey?.Dispose();
        _http?.Dispose();
        _hookWatchdog?.Dispose();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _focusTimer?.Dispose();
        _focusHudWindow?.Close();
        _developerHudWindow?.Close();
        _developerHudWindow = null;
        foreach (var win in _activeToyWindows.ToList())
        {
            try { win.Close(); } catch { }
        }
        _activeToyWindows.Clear();
        _healthService?.Dispose();
        _healthService = null;
        _notesWindow?.Close();
        _tasksWindow?.Close();
        _dashboardWindow?.Close();
        _devEventsWindow?.Close();
        _deskSense?.Dispose();
        _repoMonitor?.Dispose();
        _radarService?.Dispose();
        _reminderService?.Dispose();
        _taskReminderService?.Dispose();
        _quickCaptureWindow?.Close();
        _quickCaptureWindow = null;
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DebugLog.Write($"[Lifecycle] OnExit called with exitCode={e.ApplicationExitCode}. Stack: {Environment.StackTrace}");
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SavePetState();
        try { _notesService?.Save(); } catch { }
        _stateSaveTimer?.Stop();
        _quietWatchTimer?.Stop();
        _developerEvents?.Dispose();
        _ipcServer?.Dispose();
        _commandIpcServer?.Dispose();
        _hotkey?.Dispose();
        _focusTimer?.Dispose();
        _focusHudWindow?.Close();
        _developerHudWindow?.Close();
        _developerHudWindow = null;
        foreach (var win in _activeToyWindows.ToList())
        {
            try { win.Close(); } catch { }
        }
        _activeToyWindows.Clear();
        _healthService?.Dispose();
        _healthService = null;
        _notesWindow?.Close();
        _tasksWindow?.Close();
        _dashboardWindow?.Close();
        _devEventsWindow?.Close();
        _deskSense?.Dispose();
        _repoMonitor?.Dispose();
        _radarService?.Dispose();
        _reminderService?.Dispose();
        _taskReminderService?.Dispose();
        _quickCaptureWindow?.Close();
        _quickCaptureWindow = null;
        _http?.Dispose();
        _hookWatchdog?.Dispose();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    public static string? CheckLegacyPixelPawsConflict()
    {
        try
        {
            int currentPid = Environment.ProcessId;
            var legacy = Process.GetProcessesByName("PixelPaws")
                .Where(p => p.Id != currentPid)
                .ToList();

            if (legacy.Count > 0)
            {
                var p = legacy[0];
                string path = TryGetProcessPath(p);
                string version = TryGetProcessVersion(p);
                return $"A legacy PixelPaws process is currently running (PID {p.Id}{(string.IsNullOrEmpty(version) ? "" : $", version {version}")}).\n\n" +
                       (string.IsNullOrEmpty(path) ? "" : $"Path: {path}\n\n") +
                       "Please close PixelPaws from its system tray icon or Task Manager before starting Neku " +
                       "to prevent conflicting screen overlays, keyboard hooks, and settings.";
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Startup] Error checking legacy process: {ex.Message}");
        }

        return null;
    }

    public static void HandleAlreadyRunningInstance()
    {
        string? msg = GetRunningConflictMessage();
        if (string.IsNullOrWhiteSpace(msg))
        {
            msg = "Neku is already running.\n\nPlease check your system tray (notification area near the clock) for the Neku icon. Right-click the icon to open Settings, pause, or exit.";
        }

        DebugLog.Write($"[Startup] Instance already running: {msg}");
        MessageBox.Show(msg, "Neku - Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static string? GetRunningConflictMessage()
    {
        try
        {
            int currentPid = Environment.ProcessId;
            var nekuProcesses = Process.GetProcessesByName("Neku")
                .Where(p => p.Id != currentPid)
                .ToList();

            if (nekuProcesses.Count > 0)
            {
                var p = nekuProcesses[0];
                string path = TryGetProcessPath(p);
                string version = TryGetProcessVersion(p);
                return $"Neku is already running (PID {p.Id}{(string.IsNullOrEmpty(version) ? "" : $", version {version}")}).\n\n" +
                       (string.IsNullOrEmpty(path) ? "" : $"Path: {path}\n\n") +
                       "Check your system tray (near the clock) for the Neku icon.\n" +
                       "Right-click the tray icon to open Settings, pause, or exit.";
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Startup] Error inspecting running processes: {ex.Message}");
        }

        return null;
    }

    private static string TryGetProcessPath(Process p)
    {
        try { return p.MainModule?.FileName ?? ""; }
        catch { return ""; }
    }

    private static string TryGetProcessVersion(Process p)
    {
        try
        {
            var vi = p.MainModule?.FileVersionInfo;
            return vi?.ProductVersion ?? vi?.FileVersion ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// If launched from a background sandbox, child desktop, or non-default session desktop
    /// (e.g. IDE terminal, service subshell), re-attach the main thread to the interactive "Default"
    /// desktop before any windows, hooks, or tray icons are initialized.
    /// </summary>
    private static void EnsureInteractiveDesktop()
    {
        try
        {
            IntPtr hDesktop = Win32.OpenDesktop("Default", 0, false, Win32.DESKTOP_ALL_ACCESS);
            if (hDesktop != IntPtr.Zero)
            {
                bool switched = Win32.SetThreadDesktop(hDesktop);
                DebugLog.Write($"[Desktop] SetThreadDesktop to Default: {switched}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Desktop] Failed to switch desktop: {ex.Message}");
        }
    }
}
