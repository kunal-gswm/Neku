using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.Services.Ai;
using DesktopPet.UI;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace DesktopPet;

public partial class App : Application
{
    private Mutex?           _singleInstance;
    private SettingsService  _settingsService = new();
    private AppSettings      _settings        = new();
    private PetWindow?       _petWindow;
    private PetEngine?       _engine;
    private TrayService?     _tray;
    private SettingsWindow?  _settingsWindow;
    private KeyboardMonitor? _keyboard;
    private MouseMonitor?    _mouse;
    private SystemMonitor?   _system;
    private HookWatchdog?    _hookWatchdog;
    private UpdateService?   _updateService;
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
    private DeveloperEventsWindow? _devEventsWindow;

    // ── AI companion (built lazily, only when enabled) ──

    private HttpClient?      _http;
    private AiChatService?   _ai;
    private AiMemory?        _aiMemory;
    private ChatInputWindow? _chatWindow;
    private DispatcherTimer? _chatterTimer;
    private HotkeyService?   _hotkey;
    private readonly Random  _rng = new();

    private static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "neku_crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);


        // Crash diagnostics — capture any unhandled exception with its stack trace.
        // Mark handled so a single stray exception can't kill the user's pet.
        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(CrashLog, $"[{DateTime.Now:HH:mm:ss}] DISPATCHER\n{args.Exception}\n\n"); } catch { }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try { File.AppendAllText(CrashLog, $"[{DateTime.Now:HH:mm:ss}] DOMAIN\n{args.ExceptionObject}\n\n"); } catch { }
        };

        _singleInstance = new Mutex(initiallyOwned: true, "Neku.SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

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
        _keyboard = new KeyboardMonitor();
        _mouse    = new MouseMonitor();
        _system   = new SystemMonitor();

        // Windows drops low-level hooks silently if the UI thread ever stalls; this notices and
        // reinstalls them rather than leaving typing/scroll reactions dead for the session.
        _hookWatchdog = new HookWatchdog(_keyboard, _mouse, _system);

        var surfaceProvider = new SurfaceProvider(() => _petWindow!.Handle);
        var stateMachine    = new StateMachine(_settings, _system);
        _engine = new PetEngine(_petWindow, animator, surfaceProvider, stateMachine, _settings, _keyboard, _mouse, _system);
        _petWindow.TargetFps = _settings.TargetFps;
        _petWindow.Attach(_engine, _engine.Width, _engine.Height);

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
        _focusTimer.LoadState(_settings);
        _focusTimer.FocusCompleted += OnFocusCompleted;
        _focusTimer.BreakCompleted += OnBreakCompleted;
        _notesService = new NotesService();

        _petWindow.SetClickThrough(_settings.ClickThrough);
        _petWindow.Opacity = _settings.Opacity;

        _petWindow.SetupContextMenu(
            onFocusToggle: OnFocusToggle,
            getFocusLabel: GetFocusLabel,
            onFocusCancel: OnFocusCancel,
            isFocusActive: () => _focusTimer.IsActive,
            onToggleFocusHud: ToggleFocusHud,
            onQuickNote: OpenQuickNotes,
            onDeveloperEvents: OpenDeveloperEvents,
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
            onExit: QuitApp);

        _tray = new TrayService(
            onFocusToggle: OnFocusToggle,
            getFocusLabel: GetFocusLabel,
            onFocusCancel: OnFocusCancel,
            isFocusActive: () => _focusTimer.IsActive,
            onQuickNote: OpenQuickNotes,
            onDeveloperEvents: OpenDeveloperEvents,
            onSettings: ShowSettings,
            onPauseToggled: OnPauseToggled,
            onHideToggled: OnHideToggled,
            onClickThroughToggled: OnClickThroughToggled,
            onReducedMotionToggled: OnReducedMotionToggled,
            onQuietModeToggled: OnQuietModeToggled,
            onResetPosition: OnResetPosition,
            onQuit: QuitApp,
            onUpdate: RunUpdate,
            onAiToggled: OnAiToggled,
            onTalk: OpenChat,
            settings: _settings);

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

        _developerEvents = new DeveloperEventService(_engine, _settings, Dispatcher);
        if (_settings.EnableDeveloperEvents)
            _developerEvents.Start();


        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // AI companion: tapping the cat opens the chat box when it's enabled.
        _petWindow.ChatRequested += OpenChat;

        // Global hotkey (registered on the pet window's HWND, which now exists).
        _hotkey = new HotkeyService();
        _hotkey.Attach(_petWindow.Handle);
        _hotkey.Pressed += OpenChat;

        ApplyAiState();

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

        throw new FileNotFoundException(
            $"No pet pack found for '{pet}', and the built-in cat is missing too.");
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
                _focusTimer.StartFocus(_settings.FocusDurationMinutes);
                _engine?.Personality.OnPositiveEvent();
                if (_engine != null)
                    _engine.ShowSpeech(_engine.Personality.GetFocusStartedLine(), 3.5);
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
        _engine?.ShowSpeech("Focus session canceled.", 2.5);
    }

    private void ToggleFocusHud()
    {
        DismissOnboarding();
        if (_focusHudWindow == null)
            _focusHudWindow = new FocusHudWindow(_focusTimer);

        if (_focusHudWindow.IsVisible)
            _focusHudWindow.Hide();
        else
            _focusHudWindow.Show();
    }

    private void OnFocusCompleted()
    {
        _engine?.Personality.OnPositiveEvent();
        _engine?.TriggerDeveloperReaction(PetState.Happy, _engine.Personality.GetFocusCompletedLine());
        _petWindow?.SpawnHearts();

        var notif = new FocusNotificationWindow(
            "Focus session complete! 🎉",
            "Great work! Time for a 5m break.",
            "🍅",
            "Start 5m Break ☕",
            () => _focusTimer.StartBreak(_settings.BreakDurationMinutes));
        notif.Show();
    }

    private void OnBreakCompleted()
    {
        _engine?.TriggerDeveloperReaction(PetState.Jump, _engine.Personality.GetBreakCompletedLine());

        var notif = new FocusNotificationWindow(
            "Break finished! ✨",
            "Feeling refreshed? Ready for another session?",
            "☕",
            "Start Focus 🎯",
            () => _focusTimer.StartFocus(_settings.FocusDurationMinutes));
        notif.Show();
    }

    private void OpenQuickNotes()
    {
        DismissOnboarding();
        if (_notesWindow == null || !_notesWindow.IsLoaded)
        {
            _notesWindow = new QuickNotesWindow(_notesService);
            _notesWindow.Closed += (_, _) => _notesWindow = null;
            _notesWindow.Show();
        }
        else
        {
            _notesWindow.Activate();
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
        if (_settingsWindow is { IsLoaded: true }) { _settingsWindow.Activate(); return; }

        _settingsWindow = new SettingsWindow(
            _settings, _settingsService,
            onChanged: OnSettingsChanged,
            onForgetMemory: ForgetAiMemory,
            onTestAi: TestAiAsync,
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
            if (_settings.EnableDeveloperEvents)
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
        }
        _tray?.SetClickThrough(_settings.ClickThrough);
        _tray?.SetReducedMotion(_settings.ReducedMotion);
        _tray?.SetQuietMode(_settings.EnableQuietMode);

        if (_settings.EnableDeveloperEvents)
            _developerEvents?.Start();
        else
            _developerEvents?.Stop();

        UpdateQuietHiding();
        ApplyAiState();   // key/persona/enabled may have changed in the Settings window
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


    /// <summary>Re-sync everything to the current AI on/off state. Rebuilds the service so a
    /// new key/model/persona takes effect, and tears the chat UI down when disabled.</summary>
    private void ApplyAiState()
    {
        bool on = _settings.EnableAiCompanion;
        if (_petWindow != null) _petWindow.AiTapOpensChat = on;
        _tray?.SetAiEnabled(on);
        _ai = null;   // force a fresh provider (picks up any new key/model) on next use
        if (on)
        {
            StartChatterTimer();
        }
        else
        {
            StopChatterTimer();
            _chatWindow?.Close();
            _chatWindow = null;
            _engine?.ClearSpeech();
        }
        ApplyHotkey();
    }

    /// <summary>Register or clear the global chat hotkey to match the current settings.</summary>
    private void ApplyHotkey()
    {
        if (_hotkey == null) return;
        if (_settings.EnableAiCompanion && _settings.AiHotkeyEnabled)
            _hotkey.Register((ModifierKeys)_settings.AiHotkeyModifiers, (Key)_settings.AiHotkeyKey);
        else
            _hotkey.Unregister();
    }

    private void StartChatterTimer()
    {
        if (_chatterTimer != null) return;
        _chatterTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(6) };
        _chatterTimer.Tick += OnChatterTick;
        _chatterTimer.Start();
    }

    private void StopChatterTimer()
    {
        _chatterTimer?.Stop();
        _chatterTimer = null;
    }

    /// <summary>Every so often, maybe let the cat say something unprompted — but only when the
    /// user is present, the cat is calm and quiet, and AI + chatter are both enabled.</summary>
    private async void OnChatterTick(object? sender, EventArgs e)
    {
        if (!_settings.EnableAiCompanion || !_settings.AiProactiveChatter) return;
        if (_engine == null || _engine.Paused || _engine.IsSpeaking) return;
        if (_engine.IsQuiet) return;   // presenting / gaming / DND — don't pipe up
        if (_chatWindow is { IsLoaded: true }) return;
        if (_system != null && _system.IdleSeconds > 60) return;  // don't talk to an empty chair
        if (_rng.NextDouble() > 0.30) return;                     // ~once every ~20 min on average

        EnsureAiBuilt();
        if (_ai == null) return;
        try
        {
            var reply = await _ai.ChatterAsync(BuildChatterContext(), CancellationToken.None);
            if (_engine == null || _engine.IsSpeaking) return;    // state may have changed mid-await
            double secs = Math.Clamp(3 + reply.Text.Length * 0.06, 3, 10);
            _engine.ShowSpeech(reply.Text, secs);
            _engine.RequestEmotion(reply.Emotion);
        }
        catch { /* stay quiet on failure */ }
    }

    private string BuildChatterContext()
    {
        _system?.Poll();
        int hour = DateTime.Now.Hour;
        string tod = hour < 6 ? "it's late at night" : hour < 12 ? "it's morning"
                   : hour < 17 ? "it's afternoon" : hour < 21 ? "it's evening" : "it's night";
        string app = _system?.Foreground switch
        {
            AppContextKind.Focus  => ", the user is working in a focus app",
            AppContextKind.Browse => ", the user is browsing the web",
            AppContextKind.Play   => ", the user is in something fun",
            _ => ""
        };
        string load = _system != null && _system.CpuLoad > 0.75 ? ", the computer is working hard" : "";
        string batt = _system != null && _system.OnBattery && _system.BatteryPercent is >= 0 and <= 20
                    ? ", the laptop battery is low" : "";
        return $"{tod}{app}{load}{batt}";
    }

    private void OnAiToggled(bool enabled)
    {
        _settings.EnableAiCompanion = enabled;
        _settingsService.Save();
        ApplyAiState();
    }

    /// <summary>
    /// The one HttpClient in the app, shared by the AI providers, the cute tools and the
    /// updater. GitHub's API rejects requests without a User-Agent outright, and several
    /// keyless public APIs throttle them.
    /// </summary>
    private HttpClient Http() => _http ??= CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Neku/{AppVersion.Current} (+https://github.com/jordansbc/pixelpaws)");
        return http;
    }

    /// <summary>Build the chat service on first use. Only ever called when AI is enabled.</summary>
    private void EnsureAiBuilt()
    {
        if (_ai != null) return;
        _http ??= CreateHttp();
        _aiMemory ??= AiMemory.Load();
        IAiProvider provider = _settings.AiProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? new OllamaProvider(_http, _settings.OllamaUrl, _settings.OllamaModel)
            : new GeminiProvider(_http, _settings.AiApiKey, _settings.AiModel);

        var tools = new CuteTools(_system, _http);
        _ai = new AiChatService(_settings, provider, tools, _aiMemory);
    }

    /// <summary>Round-trip the configured brain so Settings can prove a key or a local server
    /// works, and say precisely what is wrong when it doesn't.</summary>
    private async Task<string> TestAiAsync(CancellationToken ct)
    {
        EnsureAiBuilt();
        if (_ai == null) return "Turn the AI companion on first.";

        var reply = await _ai.TestAsync(ct);
        return reply.Ok
            ? $"Works — the cat says: \u201c{reply.Text}\u201d"
            : reply.Failure switch
            {
                AiFailure.MissingKey    => "No API key set yet.",
                AiFailure.BadKey        => "That key was rejected. Double-check it.",
                AiFailure.QuotaExceeded => "Key works, but you're out of free quota right now.",
                AiFailure.UnknownModel  => $"Model not found. {reply.Detail}",
                AiFailure.Network       => $"Couldn't connect. {reply.Detail}",
                _                       => reply.Detail ?? "Something went wrong.",
            };
    }

    /// <summary>Wipe everything the cat remembers (notes + chat history), on disk and in memory.</summary>
    private void ForgetAiMemory()
    {
        (_aiMemory ??= AiMemory.Load()).Clear();
        _ai = null;   // rebuild fresh with no seeded history next time
    }

    private void OpenChat()
    {
        if (!_settings.EnableAiCompanion || _petWindow == null || _engine == null) return;
        if (_chatWindow is { IsLoaded: true }) { _chatWindow.Activate(); return; }

        EnsureAiBuilt();
        _chatWindow = new ChatInputWindow();
        _chatWindow.Submitted += OnChatSubmitted;
        _chatWindow.Closed    += (_, _) => _chatWindow = null;
        _chatWindow.PlaceNear(_petWindow.PetLeft, _petWindow.PetTop, _engine.Width, _engine.Height);
        _chatWindow.Show();
    }

    private async void OnChatSubmitted(string text)
    {
        if (_ai == null || _engine == null) return;

        // Thinking state while we wait — gentle chat bob + an ellipsis bubble.
        _engine.ShowSpeech("\u2026", 60);
        _engine.RequestEmotion(PetState.Talk);

        // Type the reply out as it streams in. The emotion tag arrives at the very end, so
        // strip any partial "[" tail while typing rather than flashing it at the user.
        var typed = new System.Text.StringBuilder();
        void OnDelta(string chunk)
        {
            if (_engine == null) return;
            typed.Append(chunk);
            string shown = StripTrailingTag(typed.ToString());
            if (shown.Length > 0) _engine.ShowSpeech(shown, 60);
        }

        try
        {
            var reply = await _ai.SendAsync(text, CancellationToken.None, OnDelta);
            double secs = Math.Clamp(3 + reply.Text.Length * 0.06, 3, 12);
            _engine.ShowSpeech(reply.Text, secs);
            _engine.RequestEmotion(reply.Emotion);

            if (!reply.Ok)
                DebugLog.Write($"AI call failed: {reply.Failure} - {reply.Detail}");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"AI call threw: {ex}");
            _engine.ShowSpeech("*mew?*", 3);
        }
    }

    /// <summary>
    /// Hide a trailing emotion tag (complete or half-streamed) while text is still arriving,
    /// so the user never watches "[jo" appear and vanish.
    /// </summary>
    private static string StripTrailingTag(string s)
    {
        int open = s.LastIndexOf('[');
        if (open < 0) return s.TrimEnd();
        // Only treat it as a tag if nothing but letters/] follow it.
        for (int i = open + 1; i < s.Length; i++)
            if (!char.IsLetter(s[i]) && s[i] != ']') return s.TrimEnd();
        return s[..open].TrimEnd();
    }

    private void QuitApp()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SavePetState();
        _stateSaveTimer?.Stop();
        _quietWatchTimer?.Stop();
        _chatterTimer?.Stop();
        _developerEvents?.Dispose();
        _hotkey?.Dispose();
        _chatWindow?.Close();
        _http?.Dispose();
        _hookWatchdog?.Dispose();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _focusTimer?.Dispose();
        _focusHudWindow?.Close();
        _notesWindow?.Close();
        _devEventsWindow?.Close();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SavePetState();
        _stateSaveTimer?.Stop();
        _quietWatchTimer?.Stop();
        _chatterTimer?.Stop();
        _developerEvents?.Dispose();
        _hotkey?.Dispose();
        _chatWindow?.Close();
        _focusTimer?.Dispose();
        _focusHudWindow?.Close();
        _notesWindow?.Close();
        _devEventsWindow?.Close();
        _http?.Dispose();
        _hookWatchdog?.Dispose();
        _keyboard?.Dispose();
        _mouse?.Dispose();
        _stretchTimer?.Stop();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
