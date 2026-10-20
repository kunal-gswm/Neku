using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DesktopPet.Services;
using DesktopPet.Services.Agent;
using DesktopPet.Services.DeskSense;
using DesktopPet.Services.Health;
using DesktopPet.Services.Radar;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace DesktopPet.UI;

public partial class DashboardWindow : Window
{
    private readonly TaskService _taskService;
    private readonly FocusTimerService _focusTimer;
    private readonly FocusHistoryService _historyService;
    private readonly AppSettings _settings;
    private readonly AnalyticsService _analytics = new();
    private readonly AgentActivityService? _agentActivityService;
    private readonly ClaudeCodeIntegrationService _claudeCodeService;
    private readonly DeveloperRadarService? _radarService;
    private readonly Action? _onToggleHud;
    private readonly SystemHealthService? _healthService;
    private readonly NotesService? _notesService;

    public event Action? QuickNotesRequested;

    private static readonly Color TimerOrangeColor = Color.FromRgb(0xFB, 0x92, 0x3C);
    private static readonly Color TimerAmberColor = Color.FromRgb(0xFB, 0xBF, 0x24);
    private static readonly Color TimerGreenColor = Color.FromRgb(0x34, 0xD3, 0x99);
    private static readonly Color TimerMutedPurpleColor = Color.FromRgb(0xA7, 0x8B, 0xFA);

    private string _currentTab = "Overview";
    private string _currentTaskFilterTab = "All";
    private string _currentAgentFilter = "all";
    private AgentSession? _selectedAgentSession;
    private TaskItem? _selectedTask;
    private bool _suppressTaskEvents = false;
    private bool _isInitialized = false;
    private DispatcherTimer? _uiRefreshTimer;

    public event Action<TaskItem>? TaskFocusRequested;
    public string CurrentTab => _currentTab;

    public DashboardWindow(
        TaskService taskService,
        FocusTimerService focusTimer,
        FocusHistoryService historyService,
        AppSettings settings,
        string? initialTab = null,
        AgentActivityService? agentActivityService = null,
        ClaudeCodeIntegrationService? claudeCodeService = null,
        DeveloperRadarService? radarService = null,
        Action? onToggleHud = null,
        SystemHealthService? healthService = null,
        NotesService? notesService = null)
    {
        _taskService = taskService;
        _focusTimer = focusTimer;
        _historyService = historyService;
        _settings = settings;
        _agentActivityService = agentActivityService;
        _claudeCodeService = claudeCodeService ?? new ClaudeCodeIntegrationService();
        _radarService = radarService;
        _onToggleHud = onToggleHud;
        _healthService = healthService;
        _notesService = notesService;

        InitializeComponent();
        _isInitialized = true;

        // Wire event listeners
        _taskService.TasksChanged += OnTasksChanged;
        _focusTimer.StateChanged += OnTimerStateChanged;
        _historyService.HistoryChanged += OnHistoryChanged;
        if (_notesService != null)
        {
            _notesService.NotesChanged += OnNotesChanged;
        }

        if (_agentActivityService != null)
        {
            _agentActivityService.HistoryChanged += OnAgentHistoryChanged;
            _agentActivityService.SessionUpdated += OnAgentSessionUpdated;
        }

        if (_radarService != null)
        {
            _radarService.RadarStateChanged += OnRadarStateChanged;
        }

        if (_healthService != null)
        {
            _healthService.HealthUpdated += OnHealthUpdated;
        }

        // Ticking timer for smooth clock countdowns
        _uiRefreshTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _uiRefreshTimer.Tick += (_, _) => UpdateTimerDisplays();
        _uiRefreshTimer.Start();

        UpdateNavRailVisibility();
        SelectTab(initialTab ?? "Overview");
        UpdateTimerDisplays();
    }

    private void OnHealthUpdated(SystemHealthSnapshot snapshot)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_currentTab == "Health")
            {
                RefreshHealth();
            }
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DesktopPet.Native.Win32.EnableImmersiveDarkMode(handle);
    }


    private void OnNotesChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_currentTab == "Overview")
            {
                RefreshOverview();
            }
        });
    }

    private void UpdateNavRailVisibility()
    {
        NavAgentBtn.Visibility = (_settings.EnableAgentCompanion || _settings.EnableDeveloperEvents) ? Visibility.Visible : Visibility.Collapsed;
        NavRadarBtn.Visibility = _settings.EnableDeveloperRadar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NavNotesBtn_Click(object sender, RoutedEventArgs e)
    {
        QuickNotesRequested?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _taskService.TasksChanged -= OnTasksChanged;
        _focusTimer.StateChanged -= OnTimerStateChanged;
        _historyService.HistoryChanged -= OnHistoryChanged;
        if (_notesService != null)
        {
            _notesService.NotesChanged -= OnNotesChanged;
        }

        if (_agentActivityService != null)
        {
            _agentActivityService.HistoryChanged -= OnAgentHistoryChanged;
            _agentActivityService.SessionUpdated -= OnAgentSessionUpdated;
        }

        if (_radarService != null)
        {
            _radarService.RadarStateChanged -= OnRadarStateChanged;
        }

        if (_healthService != null)
        {
            _healthService.HealthUpdated -= OnHealthUpdated;
        }

        _uiRefreshTimer?.Stop();
        _uiRefreshTimer = null;
    }

    private void OnTasksChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_currentTab == "Overview") RefreshOverview();
            else if (_currentTab == "Tasks") RefreshTaskList();
            else if (_currentTab == "Goals") RefreshGoals();
            else if (_currentTab == "Insights") RefreshInsights();
            PopulateFocusTaskSelector();
        });
    }

    private void OnTimerStateChanged(FocusTimerState state)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateTimerDisplays();
            if (_currentTab == "Overview") RefreshOverview();
            else if (_currentTab == "Focus") RefreshFocusView();
        });
    }

    private void OnHistoryChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_currentTab == "Overview") RefreshOverview();
            else if (_currentTab == "History") RefreshHistory();
            else if (_currentTab == "Insights") RefreshInsights();
            else if (_currentTab == "Goals") RefreshGoals();
        });
    }

    // =========================================================================
    // NAVIGATION & TAB SWITCHING
    // =========================================================================

    public void SelectTab(string tabName)
    {
        _currentTab = tabName;

        // Reset visibility of all views
        OverviewView.Visibility = tabName == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        TasksView.Visibility = tabName == "Tasks" ? Visibility.Visible : Visibility.Collapsed;
        FocusView.Visibility = tabName == "Focus" ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = tabName == "History" ? Visibility.Visible : Visibility.Collapsed;
        InsightsView.Visibility = tabName == "Insights" ? Visibility.Visible : Visibility.Collapsed;
        GoalsView.Visibility = tabName == "Goals" ? Visibility.Visible : Visibility.Collapsed;
        AgentView.Visibility = (tabName is "Agent" or "AgentActivity") ? Visibility.Visible : Visibility.Collapsed;
        RadarView.Visibility = tabName == "Radar" ? Visibility.Visible : Visibility.Collapsed;
        HealthView.Visibility = tabName == "Health" ? Visibility.Visible : Visibility.Collapsed;

        // Update nav button styles
        UpdateNavButton(NavOverviewBtn, tabName == "Overview");
        UpdateNavButton(NavTasksBtn, tabName == "Tasks");
        UpdateNavButton(NavFocusBtn, tabName == "Focus");
        UpdateNavButton(NavHistoryBtn, tabName == "History");
        UpdateNavButton(NavInsightsBtn, tabName == "Insights");
        UpdateNavButton(NavGoalsBtn, tabName == "Goals");
        UpdateNavButton(NavAgentBtn, tabName is "Agent" or "AgentActivity");
        UpdateNavButton(NavRadarBtn, tabName == "Radar");
        UpdateNavButton(NavHealthBtn, tabName == "Health");

        // Refresh selected tab contents
        switch (tabName)
        {
            case "Overview":
                RefreshOverview();
                break;
            case "Tasks":
                RefreshTaskList();
                break;
            case "Focus":
                RefreshFocusView();
                break;
            case "History":
                RefreshHistory();
                break;
            case "Insights":
                RefreshInsights();
                break;
            case "Goals":
                RefreshGoals();
                break;
            case "Agent":
            case "AgentActivity":
                RefreshAgentActivity();
                break;
            case "Radar":
                RefreshRadar();
                break;
            case "Health":
                RefreshHealth();
                break;
        }
    }

    private static readonly SolidColorBrush ActiveNavBgBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0x8B, 0x5C, 0xF6));
    private static readonly SolidColorBrush InactiveNavFgBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA));

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void UpdateNavButton(Button btn, bool isActive)
    {
        btn.Background = isActive ? ActiveNavBgBrush : Brushes.Transparent;
        btn.Foreground = isActive ? Brushes.White : InactiveNavFgBrush;
    }

    private void NavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabName)
        {
            SelectTab(tabName);
        }
    }

    // =========================================================================
    // TIMER DISPLAY & MINI-PANEL CONTROLS
    // =========================================================================

    private void UpdateTimerDisplays()
    {
        string remFormatted = _focusTimer.FormattedRemaining;
        string badgeText;
        Color badgeColor;

        if (_focusTimer.State == FocusTimerState.Focusing)
        {
            badgeText = "FOCUSING";
            badgeColor = TimerOrangeColor;
            RailPlayPauseBtn.Content = "⏸ Pause";
        }
        else if (_focusTimer.State == FocusTimerState.FocusPaused)
        {
            badgeText = "PAUSED";
            badgeColor = TimerAmberColor;
            RailPlayPauseBtn.Content = "▶ Resume";
        }
        else if (_focusTimer.State == FocusTimerState.Break)
        {
            badgeText = _focusTimer.IsLongBreak ? "LONG BREAK" : "BREAK";
            badgeColor = TimerGreenColor;
            RailPlayPauseBtn.Content = "⏸ Pause";
        }
        else if (_focusTimer.State == FocusTimerState.BreakPaused)
        {
            badgeText = "BREAK PAUSED";
            badgeColor = TimerAmberColor;
            RailPlayPauseBtn.Content = "▶ Resume";
        }
        else
        {
            badgeText = "IDLE";
            badgeColor = TimerMutedPurpleColor;
            remFormatted = $"{_settings.FocusDurationMinutes:D2}:00";
            RailPlayPauseBtn.Content = "▶ Start";
        }

        // Mini rail timer
        RailTimerBadge.Text = badgeText;
        RailTimerBadge.Foreground = new SolidColorBrush(badgeColor);
        RailTimerDigits.Text = remFormatted;
        RailActiveTaskText.Text = !string.IsNullOrEmpty(_focusTimer.ActiveTaskTitle)
            ? _focusTimer.ActiveTaskTitle
            : "No active task";

        // Big focus view timer if open
        if (_currentTab == "Focus")
        {
            FocusBigTimerDigits.Text = remFormatted;
            FocusModeText.Text = badgeText;
            FocusModeText.Foreground = new SolidColorBrush(badgeColor);

            if (_focusTimer.IsActive)
            {
                FocusMainActionBtn.Content = _focusTimer.IsPaused ? "▶ Resume Focus" : "⏸ Pause Focus";
            }
            else
            {
                FocusMainActionBtn.Content = "▶ Start Focus";
            }

            int cyclePos = (_focusTimer.CompletedSessionsInCycle % _focusTimer.LongBreakInterval) + 1;
            FocusCycleDotsText.Text = $"Pomodoro {cyclePos} of {_focusTimer.LongBreakInterval} in cycle";
        }
    }

    private void RailPlayPauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_focusTimer.State == FocusTimerState.Idle)
        {
            _focusTimer.StartFocus(_settings.FocusDurationMinutes);
        }
        else if (_focusTimer.IsPaused)
        {
            _focusTimer.Resume();
        }
        else
        {
            _focusTimer.Pause();
        }
    }

    private void RailStopBtn_Click(object sender, RoutedEventArgs e)
    {
        _focusTimer.Cancel();
    }

    // =========================================================================
    // 1. OVERVIEW VIEW
    // =========================================================================

    private void RefreshOverview()
    {
        if (!_isInitialized) return;
        OverviewDateText.Text = DateTime.Today.ToString("dddd, MMMM d, yyyy");

        var allTasks = _taskService.Tasks;
        var allSessions = _historyService.Sessions;

        // Focus Today
        double todayFocusMins = _historyService.GetTodayFocusMinutes();
        MetricFocusTodayText.Text = $"{(int)todayFocusMins}m";
        int focusGoal = _settings.DailyFocusGoalMinutes;
        int focusPct = focusGoal > 0 ? (int)Math.Min(100, Math.Round((todayFocusMins / focusGoal) * 100)) : 0;
        MetricFocusGoalSubText.Text = $"Goal: {focusGoal}m ({focusPct}%)";

        // Tasks Done Today
        int tasksDoneToday = allTasks.Count(t => t.IsCompleted && t.CompletedAt.HasValue && TaskItem.ToLocalDate(t.CompletedAt.Value) == DateTime.Today);
        MetricTasksDoneText.Text = tasksDoneToday.ToString();
        MetricTasksGoalSubText.Text = $"Goal: {_settings.DailyTaskGoal} tasks";

        // Tasks Remaining & Overdue
        int pendingCount = allTasks.Count(t => !t.IsCompleted);
        int overdueCount = allTasks.Count(t => t.IsOverdue);
        MetricTasksPendingText.Text = pendingCount.ToString();
        MetricOverdueSubText.Text = overdueCount > 0 ? $"⚠️ {overdueCount} overdue" : "0 overdue";

        // Streaks
        var (currentStreak, longestStreak) = AnalyticsService.CalculateStreaks(allSessions, allTasks);
        MetricStreakText.Text = $"{currentStreak} days";
        MetricStreakSubText.Text = $"Best: {longestStreak} days";

        // Overview Quick Focus CTA
        if (_focusTimer.IsActive)
        {
            OverviewQuickFocusBtn.Content = _focusTimer.IsPaused ? "▶ Resume Focus" : "⏱ View Active Focus";
        }
        else
        {
            OverviewQuickFocusBtn.Content = $"🎯 Start {_settings.FocusDurationMinutes}m Focus";
        }

        // Today's Actionable Tasks
        var todayTasks = allTasks
            .Where(t => (!t.IsCompleted && (t.DueDate == null || TaskItem.ToLocalDate(t.DueDate.Value) <= DateTime.Today)) ||
                        (t.IsCompleted && t.CompletedAt.HasValue && TaskItem.ToLocalDate(t.CompletedAt.Value) == DateTime.Today))
            .OrderBy(t => t.IsCompleted)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
            .Take(8)
            .Select(t => new TodayTaskDisplayItem(t))
            .ToList();

        OverviewTodayListBox.ItemsSource = todayTasks;
        int pendingTodayCount = todayTasks.Count(t => !t.IsCompleted);
        OverviewTodayTasksBadge.Text = $" ({pendingTodayCount} pending)";
        OverviewTasksEmptyBorder.Visibility = todayTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Recent Quick Notes
        if (_notesService != null)
        {
            var recentNotes = _notesService.Notes
                .OrderByDescending(n => n.UpdatedAt)
                .Take(3)
                .Select(n => new NoteOverviewItem(n))
                .ToList();
            OverviewNotesListBox.ItemsSource = recentNotes;
            OverviewNotesEmptyBorder.Visibility = recentNotes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            OverviewNotesEmptyBorder.Visibility = Visibility.Visible;
        }

        // Recent sessions list (last 5)
        OverviewRecentListBox.ItemsSource = allSessions.Take(5).ToList();

        DrawWeeklyChart();
    }

    internal void OverviewTaskCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is TodayTaskDisplayItem item)
        {
            _taskService.ToggleComplete(item.Task.Id);
            RefreshOverview();
            if (_currentTab == "Tasks") RefreshTaskList();
        }
    }

    internal void OverviewTaskFocus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TodayTaskDisplayItem item)
        {
            TaskFocusRequested?.Invoke(item.Task);
        }
    }

    private void ViewAllTasks_Click(object sender, RoutedEventArgs e)
    {
        SelectTab("Tasks");
    }

    private void OverviewNewNote_Click(object sender, RoutedEventArgs e)
    {
        QuickNotesRequested?.Invoke();
    }

    private void OverviewOpenNotes_Click(object sender, RoutedEventArgs e)
    {
        QuickNotesRequested?.Invoke();
    }

    private void OverviewNoteItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        QuickNotesRequested?.Invoke();
    }

    private void DrawWeeklyChart()
    {
        OverviewWeeklyCanvas.Children.Clear();

        double width = OverviewWeeklyCanvas.ActualWidth;
        double height = OverviewWeeklyCanvas.ActualHeight;
        if (width <= 10 || height <= 10) return;

        var summary = _analytics.GenerateSummary(_historyService.Sessions, _taskService.Tasks, TimeRangePreset.Last7Days);
        var days = summary.DailyBreakdown;
        if (days.Count == 0) return;

        double maxMins = days.Max(d => d.FocusMinutes);
        if (maxMins < 60) maxMins = 60; // scale minimum to 60 mins

        double barWidth = Math.Max(12, (width / days.Count) - 14);
        double maxBarHeight = height - 32;

        var barBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFB923C")); // Orange
        var todayBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF97316"));
        var textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA78BFA"));

        for (int i = 0; i < days.Count; i++)
        {
            var day = days[i];
            double x = i * (width / days.Count) + ((width / days.Count - barWidth) / 2);
            double barHeight = Math.Max(3, (day.FocusMinutes / maxMins) * maxBarHeight);
            double y = height - 22 - barHeight;

            // Bar rectangle
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 4,
                RadiusY = 4,
                Fill = day.Date == DateTime.Today ? todayBrush : barBrush,
                ToolTip = $"{day.Date:MMM d}: {(int)day.FocusMinutes} mins ({day.CompletedSessions} sessions)",
                Cursor = Cursors.Hand
            };
            rect.MouseDown += (_, _) => SelectTab("History");
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            OverviewWeeklyCanvas.Children.Add(rect);

            // Day label
            var label = new TextBlock
            {
                Text = day.Date.ToString("ddd"),
                FontSize = 9,
                Foreground = textBrush,
                Width = width / days.Count,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(label, i * (width / days.Count));
            Canvas.SetTop(label, height - 18);
            OverviewWeeklyCanvas.Children.Add(label);

            // Minute value on bar if > 0
            if (day.FocusMinutes > 0)
            {
                var valLabel = new TextBlock
                {
                    Text = $"{(int)day.FocusMinutes}m",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Width = width / days.Count,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(valLabel, i * (width / days.Count));
                Canvas.SetTop(valLabel, Math.Max(0, y - 14));
                OverviewWeeklyCanvas.Children.Add(valLabel);
            }
        }
    }

    private void OverviewWeeklyCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawWeeklyChart();
    }

    private void CardFocus_MouseDown(object sender, MouseButtonEventArgs e) => SelectTab("Focus");
    private void CardTasks_MouseDown(object sender, MouseButtonEventArgs e) => SelectTab("Tasks");
    private void CardGoals_MouseDown(object sender, MouseButtonEventArgs e) => SelectTab("Goals");
    private void OverdueText_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SelectTab("Tasks");
        _currentTaskFilterTab = "Overdue";
        RefreshTaskList();
    }
    private void ViewAllHistory_Click(object sender, RoutedEventArgs e) => SelectTab("History");

    private void OverviewQuickTaskBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (OverviewQuickTaskBox.Text == "Add a new task for today...")
        {
            OverviewQuickTaskBox.Text = "";
        }
    }

    private void OverviewQuickTaskBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OverviewQuickTaskBox.Text))
        {
            OverviewQuickTaskBox.Text = "Add a new task for today...";
        }
    }

    private void OverviewQuickTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateQuickTask();
        }
    }

    private void OverviewQuickAddBtn_Click(object sender, RoutedEventArgs e)
    {
        CreateQuickTask();
    }

    private void CreateQuickTask()
    {
        string title = OverviewQuickTaskBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(title) || title == "Add a new task for today...") return;

        var task = _taskService.Create(
            title: title,
            dueDate: DateTime.Today,
            estimatedMinutes: 25);

        OverviewQuickTaskBox.Text = "Add a new task for today...";
        RefreshOverview();
    }

    private void OverviewQuickFocusBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_focusTimer.IsActive)
        {
            SelectTab("Focus");
        }
        else
        {
            _focusTimer.StartFocus(_settings.FocusDurationMinutes);
            SelectTab("Focus");
        }
    }

    // =========================================================================
    // 2. TASKS VIEW
    // =========================================================================

    private void RefreshTaskList()
    {
        if (!_isInitialized) return;
        var allTasks = _taskService.Tasks;

        // Update Project & Tag ComboBox options
        PopulateProjectFilterCombo();
        PopulateTagFilterCombo();

        // 1. Filter by Tab
        IEnumerable<TaskItem> filtered = _currentTaskFilterTab switch
        {
            "Today" => allTasks.Where(t => !t.IsCompleted && t.DueDate.HasValue && TaskItem.ToLocalDate(t.DueDate.Value) <= DateTime.Today),
            "Upcoming" => allTasks.Where(t => !t.IsCompleted && t.DueDate.HasValue && TaskItem.ToLocalDate(t.DueDate.Value) > DateTime.Today),
            "Inbox" => allTasks.Where(t => !t.IsCompleted && t.Status == TaskItemStatus.Inbox),
            "InProgress" => allTasks.Where(t => !t.IsCompleted && t.Status == TaskItemStatus.InProgress),
            "Overdue" => allTasks.Where(t => t.IsOverdue),
            "Completed" => allTasks.Where(t => t.IsCompleted),
            _ => allTasks.Where(t => !t.IsCompleted) // All
        };

        // 2. Search Query
        string query = TaskSearchBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Project.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        // 3. Project Filter
        string? selectedProj = TaskProjectCombo.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(selectedProj) && selectedProj != "All Projects")
        {
            filtered = filtered.Where(t => string.Equals(t.Project, selectedProj, StringComparison.OrdinalIgnoreCase));
        }

        // 3b. Tag Filter
        string? selectedTag = TaskTagCombo.SelectedItem?.ToString();
        if (!string.IsNullOrEmpty(selectedTag) && selectedTag != "All Tags")
        {
            filtered = filtered.Where(t => t.Tags.Any(tag => string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase)));
        }

        // 4. Sort
        string sortChoice = (TaskSortCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Due Date";
        filtered = sortChoice switch
        {
            "Priority" => filtered.OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate ?? DateTime.MaxValue),
            "Newest" => filtered.OrderByDescending(t => t.CreatedAt),
            "Title" => filtered.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderBy(t => t.DueDate.HasValue ? 0 : 1)
                         .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                         .ThenByDescending(t => t.Priority)
        };

        var list = filtered.ToList();
        TasksListBox.ItemsSource = list;

        // Empty state handling
        if (list.Count == 0)
        {
            TaskEmptyBorder.Visibility = Visibility.Visible;
            TasksListBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            TaskEmptyBorder.Visibility = Visibility.Collapsed;
            TasksListBox.Visibility = Visibility.Visible;
        }

        // Maintain selection
        if (_selectedTask != null && list.Any(t => t.Id == _selectedTask.Id))
        {
            TasksListBox.SelectedItem = list.First(t => t.Id == _selectedTask.Id);
        }
        else if (list.Count > 0)
        {
            TasksListBox.SelectedIndex = 0;
        }
        else
        {
            TasksListBox.SelectedItem = null;
            ShowTaskDetail(null);
        }

        UpdateTaskTabCounts();
    }

    private void PopulateProjectFilterCombo()
    {
        string? current = TaskProjectCombo.SelectedItem?.ToString();
        var projects = _taskService.GetProjects().ToList();
        projects.Insert(0, "All Projects");

        TaskProjectCombo.ItemsSource = projects;
        if (current != null && projects.Contains(current))
        {
            TaskProjectCombo.SelectedItem = current;
        }
        else
        {
            TaskProjectCombo.SelectedIndex = 0;
        }
    }

    private void PopulateTagFilterCombo()
    {
        string? current = TaskTagCombo.SelectedItem?.ToString();
        var tags = _taskService.GetTags().ToList();
        tags.Insert(0, "All Tags");

        TaskTagCombo.ItemsSource = tags;
        if (current != null && tags.Contains(current))
        {
            TaskTagCombo.SelectedItem = current;
        }
        else
        {
            TaskTagCombo.SelectedIndex = 0;
        }
    }

    private void TasksExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"neku_tasks_{DateTime.Now:yyyyMMdd}.csv"
        };
        if (sfd.ShowDialog() == true)
        {
            string csv = AnalyticsService.ExportTasksToCsv(_taskService.Tasks);
            System.IO.File.WriteAllText(sfd.FileName, csv);
            MessageBox.Show("Tasks exported successfully to CSV!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void TasksExportJson_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"neku_tasks_{DateTime.Now:yyyyMMdd}.json"
        };
        if (sfd.ShowDialog() == true)
        {
            string json = AnalyticsService.ExportTasksToJson(_taskService.Tasks);
            System.IO.File.WriteAllText(sfd.FileName, json);
            MessageBox.Show("Tasks exported successfully to JSON!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void UpdateTaskTabCounts()
    {
        var all = _taskService.Tasks;
        int today = all.Count(t => !t.IsCompleted && t.DueDate.HasValue && TaskItem.ToLocalDate(t.DueDate.Value) <= DateTime.Today);
        int upcoming = all.Count(t => !t.IsCompleted && t.DueDate.HasValue && TaskItem.ToLocalDate(t.DueDate.Value) > DateTime.Today);
        int inbox = all.Count(t => !t.IsCompleted && t.Status == TaskItemStatus.Inbox);
        int inProg = all.Count(t => !t.IsCompleted && t.Status == TaskItemStatus.InProgress);
        int overdue = all.Count(t => t.IsOverdue);
        int allCount = all.Count(t => !t.IsCompleted);
        int done = all.Count(t => t.IsCompleted);

        TaskTabToday.Content = today > 0 ? $"☀️ Today ({today})" : "☀️ Today";
        TaskTabUpcoming.Content = upcoming > 0 ? $"📅 Upcoming ({upcoming})" : "📅 Upcoming";
        TaskTabInbox.Content = inbox > 0 ? $"📥 Inbox ({inbox})" : "📥 Inbox";
        TaskTabInProgress.Content = inProg > 0 ? $"⚡ In Progress ({inProg})" : "⚡ In Progress";
        TaskTabOverdue.Content = overdue > 0 ? $"⚠️ Overdue ({overdue})" : "⚠️ Overdue";
        TaskTabAll.Content = allCount > 0 ? $"📋 All ({allCount})" : "📋 All";
        TaskTabDone.Content = done > 0 ? $"✅ Done ({done})" : "✅ Done";

        UpdateFilterChip(TaskTabToday, _currentTaskFilterTab == "Today");
        UpdateFilterChip(TaskTabUpcoming, _currentTaskFilterTab == "Upcoming");
        UpdateFilterChip(TaskTabInbox, _currentTaskFilterTab == "Inbox");
        UpdateFilterChip(TaskTabInProgress, _currentTaskFilterTab == "InProgress");
        UpdateFilterChip(TaskTabOverdue, _currentTaskFilterTab == "Overdue");
        UpdateFilterChip(TaskTabAll, _currentTaskFilterTab == "All");
        UpdateFilterChip(TaskTabDone, _currentTaskFilterTab == "Completed");
    }

    private static void UpdateFilterChip(Button btn, bool isActive)
    {
        var activeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF452C63"));
        var activeBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B5CF6"));
        var activeFg = Brushes.White;

        var inactiveBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF221B30"));
        var inactiveBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF382F4A"));
        var inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA78BFA"));

        btn.Background = isActive ? activeBg : inactiveBg;
        btn.BorderBrush = isActive ? activeBorder : inactiveBorder;
        btn.Foreground = isActive ? activeFg : inactiveFg;
        btn.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void TaskTab_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button ?? e.Source as Button;
        if (btn != null && btn.Tag is string tabTag)
        {
            _currentTaskFilterTab = tabTag;
            RefreshTaskList();
        }
    }

    private void TaskSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        RefreshTaskList();
    }

    private void TaskFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        RefreshTaskList();
    }

    private void TasksListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (TasksListBox.SelectedItem is TaskItem item)
        {
            ShowTaskDetail(item);
        }
        else
        {
            ShowTaskDetail(null);
        }
    }

    private void ShowTaskDetail(TaskItem? task)
    {
        _selectedTask = task;
        if (task == null)
        {
            TaskDetailScrollViewer.Visibility = Visibility.Collapsed;
            TaskNoSelectionText.Visibility = Visibility.Visible;
            return;
        }

        _suppressTaskEvents = true;

        DetailCompleteCheck.IsChecked = task.IsCompleted;
        DetailCompleteCheck.Content = task.IsCompleted ? "Completed ✅" : "Mark Completed";

        DetailTitleBox.Text = task.Title;
        DetailDescBox.Text = task.Description;

        // Status Combo
        DetailStatusCombo.SelectedIndex = task.Status switch
        {
            TaskItemStatus.Inbox => 0,
            TaskItemStatus.Todo => 1,
            TaskItemStatus.InProgress => 2,
            TaskItemStatus.Completed => 3,
            _ => 1
        };

        // Priority Combo
        DetailPriorityCombo.SelectedIndex = task.Priority switch
        {
            TaskPriority.Low => 0,
            TaskPriority.Normal => 1,
            TaskPriority.High => 2,
            TaskPriority.Urgent => 3,
            _ => 1
        };

        DetailProjectBox.Text = task.Project;
        DetailTagsBox.Text = string.Join(", ", task.Tags);
        DetailDueDatePicker.SelectedDate = task.DueDate;

        // Recurrence Combo
        DetailRecurrenceCombo.SelectedIndex = task.RecurrenceRule switch
        {
            TaskRecurrenceRule.Daily => 1,
            TaskRecurrenceRule.Weekly => 2,
            _ => 0
        };

        // Estimated minutes
        DetailEstMinutesCombo.SelectedIndex = task.EstimatedMinutes switch
        {
            25 => 1,
            50 => 2,
            75 => 3,
            100 => 4,
            _ => 0
        };

        // Subtasks list
        DetailSubtasksItemsControl.ItemsSource = task.Subtasks.ToList();

        // Focus statistics
        DetailFocusStatsText.Text = $"🍅 {task.CompletedPomodoros} focus sessions ({task.TotalFocusMinutes} mins focused)";
        DetailStartFocusBtn.Content = $"🎯 Start Focus ({_settings.FocusDurationMinutes}m)";

        _suppressTaskEvents = false;

        TaskDetailScrollViewer.Visibility = Visibility.Visible;
        TaskNoSelectionText.Visibility = Visibility.Collapsed;
    }

    private void TaskAddNewBtn_Click(object sender, RoutedEventArgs e)
    {
        DateTime? due = _currentTaskFilterTab == "Today" ? DateTime.Today
            : (_currentTaskFilterTab == "Upcoming" ? DateTime.Today.AddDays(1) : (DateTime?)null);

        var task = _taskService.Create(
            title: "New Task",
            dueDate: due,
            estimatedMinutes: 25);

        _selectedTask = task;
        RefreshTaskList();
        DetailTitleBox.Focus();
        DetailTitleBox.SelectAll();
    }

    private void TaskItemCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is TaskItem task)
        {
            _taskService.ToggleComplete(task.Id);
            RefreshTaskList();
        }
    }

    private void DetailCompleteCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressTaskEvents || _selectedTask == null) return;
        _taskService.ToggleComplete(_selectedTask.Id);
        RefreshTaskList();
    }

    private void DetailField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressTaskEvents || _selectedTask == null) return;

        _taskService.Update(_selectedTask.Id, t =>
        {
            t.Title = DetailTitleBox.Text.Trim();
            t.Description = DetailDescBox.Text.Trim();
            t.Project = DetailProjectBox.Text.Trim();
            t.Tags = DetailTagsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrEmpty(tag))
                .Distinct()
                .ToList();
        });
    }

    private void DetailStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTaskEvents || _selectedTask == null) return;

        var status = DetailStatusCombo.SelectedIndex switch
        {
            0 => TaskItemStatus.Inbox,
            1 => TaskItemStatus.Todo,
            2 => TaskItemStatus.InProgress,
            3 => TaskItemStatus.Completed,
            _ => TaskItemStatus.Todo
        };

        _taskService.Update(_selectedTask.Id, t => t.Status = status);
    }

    private void DetailPriority_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTaskEvents || _selectedTask == null) return;

        var priority = DetailPriorityCombo.SelectedIndex switch
        {
            0 => TaskPriority.Low,
            1 => TaskPriority.Normal,
            2 => TaskPriority.High,
            3 => TaskPriority.Urgent,
            _ => TaskPriority.Normal
        };

        _taskService.Update(_selectedTask.Id, t => t.Priority = priority);
    }

    private void DetailDueDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTaskEvents || _selectedTask == null) return;
        _taskService.Update(_selectedTask.Id, t => t.DueDate = DetailDueDatePicker.SelectedDate);
    }

    private void DetailRecurrence_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTaskEvents || _selectedTask == null) return;

        var rule = DetailRecurrenceCombo.SelectedIndex switch
        {
            1 => TaskRecurrenceRule.Daily,
            2 => TaskRecurrenceRule.Weekly,
            _ => TaskRecurrenceRule.None
        };

        _taskService.Update(_selectedTask.Id, t => t.RecurrenceRule = rule);
    }

    private void DetailEstMinutes_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTaskEvents || _selectedTask == null) return;

        int mins = DetailEstMinutesCombo.SelectedIndex switch
        {
            1 => 25,
            2 => 50,
            3 => 75,
            4 => 100,
            _ => 0
        };

        _taskService.Update(_selectedTask.Id, t => t.EstimatedMinutes = mins);
    }

    private void DetailNewSubtaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddSubtaskFromDetail();
        }
    }

    private void DetailAddSubtaskBtn_Click(object sender, RoutedEventArgs e)
    {
        AddSubtaskFromDetail();
    }

    private void AddSubtaskFromDetail()
    {
        if (_selectedTask == null) return;
        string text = DetailNewSubtaskBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;

        _taskService.AddSubtask(_selectedTask.Id, text);
        DetailNewSubtaskBox.Text = "";
        DetailSubtasksItemsControl.ItemsSource = _selectedTask.Subtasks.ToList();
    }

    private void SubtaskCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;
        if (sender is CheckBox cb && cb.DataContext is SubtaskItem subtask)
        {
            _taskService.ToggleSubtask(_selectedTask.Id, subtask.Id);
            DetailSubtasksItemsControl.ItemsSource = _selectedTask.Subtasks.ToList();
        }
    }

    private void SubtaskDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;
        if (sender is Button btn && btn.DataContext is SubtaskItem subtask)
        {
            _taskService.DeleteSubtask(_selectedTask.Id, subtask.Id);
            DetailSubtasksItemsControl.ItemsSource = _selectedTask.Subtasks.ToList();
        }
    }

    private void DetailStartFocusBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;
        if (TaskFocusRequested != null)
        {
            TaskFocusRequested.Invoke(_selectedTask);
        }
        else
        {
            _focusTimer.StartFocus(_settings.FocusDurationMinutes, _selectedTask.Id, _selectedTask.Title, _selectedTask.Project);
        }
        SelectTab("Focus");
    }

    private void DetailDeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;

        var result = MessageBox.Show(
            $"Delete task '{_selectedTask.Title}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            string id = _selectedTask.Id;
            _selectedTask = null;
            _taskService.Delete(id);
            RefreshTaskList();
        }
    }

    private void DetailClearCompletedBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to clear all completed tasks?",
            "Clear Completed Tasks",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _taskService.ClearCompleted();
            RefreshTaskList();
        }
    }

    // =========================================================================
    // 3. FOCUS VIEW
    // =========================================================================

    private void RefreshFocusView()
    {
        if (!_isInitialized) return;
        PopulateFocusTaskSelector();

        FocusDurBox.Text = _settings.FocusDurationMinutes.ToString();
        FocusShortBreakBox.Text = _settings.BreakDurationMinutes.ToString();
        FocusLongBreakBox.Text = _settings.LongBreakDurationMinutes.ToString();
        FocusAutoBreakCheck.IsChecked = _settings.AutoStartBreaks;
        FocusAutoFocusCheck.IsChecked = _settings.AutoStartFocus;

        FocusNotifyFocusCheck.IsChecked = _settings.EnableFocusCompletedReminders;
        FocusNotifyBreakCheck.IsChecked = _settings.EnableBreakCompletedReminders;
        FocusNotifyUpcomingCheck.IsChecked = _settings.EnableUpcomingTaskReminders;
        FocusNotifyOverdueCheck.IsChecked = _settings.EnableOverdueTaskReminders;

        UpdateTimerDisplays();
    }

    private void FocusReminderToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnableFocusCompletedReminders = FocusNotifyFocusCheck.IsChecked ?? true;
        _settings.EnableBreakCompletedReminders = FocusNotifyBreakCheck.IsChecked ?? true;
        _settings.EnableUpcomingTaskReminders = FocusNotifyUpcomingCheck.IsChecked ?? true;
        _settings.EnableOverdueTaskReminders = FocusNotifyOverdueCheck.IsChecked ?? true;
    }

    private void PopulateFocusTaskSelector()
    {
        var tasks = _taskService.Tasks.Where(t => !t.IsCompleted).ToList();
        var titles = tasks.Select(t => t.Title).ToList();
        titles.Insert(0, "(None - Free Focus)");

        FocusTaskSelectorCombo.ItemsSource = titles;

        if (!string.IsNullOrEmpty(_focusTimer.ActiveTaskTitle))
        {
            FocusTaskSelectorCombo.SelectedItem = _focusTimer.ActiveTaskTitle;
        }
        else
        {
            FocusTaskSelectorCombo.SelectedIndex = 0;
        }
    }

    private void FocusTaskSelectorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_focusTimer.IsActive) return; // Do not switch active running task mid-flight

        string? selected = FocusTaskSelectorCombo.SelectedItem?.ToString();
        if (selected == null || selected.StartsWith("(None"))
        {
            _focusTimer.ClearActiveTask();
        }
        else
        {
            var task = _taskService.Tasks.FirstOrDefault(t => t.Title == selected);
            if (task != null)
            {
                _focusTimer.ActiveTaskId = task.Id;
                _focusTimer.ActiveTaskTitle = task.Title;
                _focusTimer.ActiveTaskProject = task.Project;
            }
        }
    }

    private void FocusMainActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_focusTimer.State == FocusTimerState.Idle)
        {
            _focusTimer.StartFocus(_settings.FocusDurationMinutes, _focusTimer.ActiveTaskId, _focusTimer.ActiveTaskTitle, _focusTimer.ActiveTaskProject);
        }
        else if (_focusTimer.IsPaused)
        {
            _focusTimer.Resume();
        }
        else
        {
            _focusTimer.Pause();
        }
    }

    private void FocusResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _focusTimer.Reset();
    }

    private void FocusCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        _focusTimer.Cancel();
    }

    private void FocusShortBreakBtn_Click(object sender, RoutedEventArgs e)
    {
        _focusTimer.StartBreak(isLongBreak: false);
    }

    private void FocusLongBreakBtn_Click(object sender, RoutedEventArgs e)
    {
        _focusTimer.StartBreak(isLongBreak: true);
    }

    private void FocusDurBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(FocusDurBox.Text, out int m) && m > 0)
        {
            _settings.FocusDurationMinutes = m;
            _focusTimer.FocusDurationMinutes = m;
        }
    }

    private void FocusShortBreakBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(FocusShortBreakBox.Text, out int m) && m > 0)
        {
            _settings.BreakDurationMinutes = m;
            _focusTimer.BreakDurationMinutes = m;
        }
    }

    private void FocusLongBreakBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(FocusLongBreakBox.Text, out int m) && m > 0)
        {
            _settings.LongBreakDurationMinutes = m;
            _focusTimer.LongBreakDurationMinutes = m;
        }
    }

    private void FocusAutoBreakCheck_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = FocusAutoBreakCheck.IsChecked ?? false;
        _settings.AutoStartBreaks = isChecked;
        _focusTimer.AutoStartBreaks = isChecked;
    }

    private void FocusAutoFocusCheck_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = FocusAutoFocusCheck.IsChecked ?? false;
        _settings.AutoStartFocus = isChecked;
        _focusTimer.AutoStartFocus = isChecked;
    }

    // =========================================================================
    // 4. HISTORY VIEW
    // =========================================================================

    private void RefreshHistory()
    {
        if (!_isInitialized) return;
        string? rangeChoice = (HistoryRangeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var preset = rangeChoice switch
        {
            "Yesterday" => TimeRangePreset.Yesterday,
            "Last 7 Days" => TimeRangePreset.Last7Days,
            "Last 30 Days" => TimeRangePreset.Last30Days,
            "All Time" => TimeRangePreset.AllTime,
            "Custom Range…" => TimeRangePreset.Custom,
            _ => TimeRangePreset.Today
        };

        DateTime? customStart = null;
        DateTime? customEnd = null;

        if (preset == TimeRangePreset.Custom)
        {
            if (HistoryCustomDatePanel != null)
                HistoryCustomDatePanel.Visibility = Visibility.Visible;

            if (HistoryStartDatePicker != null && !HistoryStartDatePicker.SelectedDate.HasValue)
                HistoryStartDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
            if (HistoryEndDatePicker != null && !HistoryEndDatePicker.SelectedDate.HasValue)
                HistoryEndDatePicker.SelectedDate = DateTime.Today;

            customStart = HistoryStartDatePicker?.SelectedDate;
            customEnd = HistoryEndDatePicker?.SelectedDate;

            if (customStart.HasValue && customEnd.HasValue && customEnd.Value < customStart.Value)
            {
                customEnd = customStart.Value;
                if (HistoryEndDatePicker != null)
                    HistoryEndDatePicker.SelectedDate = customEnd;
            }
        }
        else
        {
            if (HistoryCustomDatePanel != null)
                HistoryCustomDatePanel.Visibility = Visibility.Collapsed;
        }

        var (start, end) = AnalyticsService.GetDateRange(preset, customStart, customEnd);
        var sessions = _historyService.GetSessionsInDateRange(start, end);

        // Filter by Type
        string? typeChoice = (HistoryTypeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrEmpty(typeChoice) && typeChoice != "All Types")
        {
            sessions = typeChoice switch
            {
                "Focus" => sessions.Where(s => s.SessionType == FocusSessionType.Focus).ToList(),
                "Short Break" => sessions.Where(s => s.SessionType == FocusSessionType.ShortBreak).ToList(),
                "Long Break" => sessions.Where(s => s.SessionType == FocusSessionType.LongBreak).ToList(),
                _ => sessions
            };
        }

        // Filter by Status
        string? statusChoice = (HistoryStatusCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrEmpty(statusChoice) && statusChoice != "All Status")
        {
            sessions = statusChoice switch
            {
                "Completed" => sessions.Where(s => s.Status == FocusSessionStatus.Completed).ToList(),
                "Interrupted" => sessions.Where(s => s.Status == FocusSessionStatus.Interrupted).ToList(),
                "Cancelled" => sessions.Where(s => s.Status == FocusSessionStatus.Cancelled).ToList(),
                _ => sessions
            };
        }

        HistoryDataGrid.ItemsSource = sessions;

        double focusMins = Math.Round(sessions.Where(s => s.SessionType == FocusSessionType.Focus && s.Status == FocusSessionStatus.Completed)
                                              .Sum(s => s.ActualSeconds) / 60.0, 1);
        int sessionCount = sessions.Count(s => s.SessionType == FocusSessionType.Focus && s.Status == FocusSessionStatus.Completed);

        HistorySummaryStatsText.Text = $"Total: {focusMins}m focus across {sessionCount} completed sessions";
    }

    private void HistoryCustomDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryCustomDatePanel?.Visibility == Visibility.Visible)
        {
            RefreshHistory();
        }
    }

    private void HistoryResetRangeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryRangeCombo != null)
            HistoryRangeCombo.SelectedIndex = 0; // Today
    }

    private void HistoryRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshHistory();
    private void HistoryTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshHistory();
    private void HistoryStatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshHistory();

    private void HistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is FocusSessionRecord record)
        {
            HistoryNotesTextBox.Text = record.Notes ?? "";
        }
        else
        {
            HistoryNotesTextBox.Text = "";
        }
    }

    private void HistorySaveNotesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryDataGrid.SelectedItem is FocusSessionRecord record)
        {
            string notes = HistoryNotesTextBox.Text ?? "";
            _historyService.UpdateNotes(record.Id, notes);
            RefreshHistory();
        }
    }

    private void ProductivityBackup_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"neku_productivity_backup_{DateTime.Now:yyyyMMdd}.json"
        };

        if (sfd.ShowDialog() == true)
        {
            string json = AnalyticsService.ExportFullBackupJson(_taskService.Tasks, _historyService.Sessions, _settings);
            System.IO.File.WriteAllText(sfd.FileName, json);
            MessageBox.Show("Full productivity data backup exported successfully!", "Backup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void HistoryExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"neku_focus_history_{DateTime.Now:yyyyMMdd}.csv"
        };

        if (sfd.ShowDialog() == true)
        {
            string csv = AnalyticsService.ExportSessionsToCsv(_historyService.Sessions);
            System.IO.File.WriteAllText(sfd.FileName, csv);
            MessageBox.Show("Focus history exported successfully to CSV!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void HistoryExportJson_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = $"neku_focus_history_{DateTime.Now:yyyyMMdd}.json"
        };

        if (sfd.ShowDialog() == true)
        {
            string json = AnalyticsService.ExportSessionsToJson(_historyService.Sessions);
            System.IO.File.WriteAllText(sfd.FileName, json);
            MessageBox.Show("Focus history exported successfully to JSON!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void HistoryClearAll_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to clear all focus session history? This action cannot be undone.",
            "Clear History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _historyService.ClearAll();
            RefreshHistory();
        }
    }

    // =========================================================================
    // 5. INSIGHTS VIEW
    // =========================================================================

    private void RefreshInsights()
    {
        if (!_isInitialized) return;
        var preset = (InsightsRangeCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() switch
        {
            "Today" => TimeRangePreset.Today,
            "Last 30 Days" => TimeRangePreset.Last30Days,
            "This Month" => TimeRangePreset.ThisMonth,
            "All Time" => TimeRangePreset.AllTime,
            "Custom Range…" => TimeRangePreset.Custom,
            _ => TimeRangePreset.Last7Days
        };

        DateTime? customStart = null;
        DateTime? customEnd = null;

        if (preset == TimeRangePreset.Custom)
        {
            if (InsightsCustomDatePanel != null)
                InsightsCustomDatePanel.Visibility = Visibility.Visible;

            if (InsightsStartDatePicker != null && !InsightsStartDatePicker.SelectedDate.HasValue)
                InsightsStartDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
            if (InsightsEndDatePicker != null && !InsightsEndDatePicker.SelectedDate.HasValue)
                InsightsEndDatePicker.SelectedDate = DateTime.Today;

            customStart = InsightsStartDatePicker?.SelectedDate;
            customEnd = InsightsEndDatePicker?.SelectedDate;

            if (customStart.HasValue && customEnd.HasValue && customEnd.Value < customStart.Value)
            {
                customEnd = customStart.Value;
                if (InsightsEndDatePicker != null)
                    InsightsEndDatePicker.SelectedDate = customEnd;
            }
        }
        else
        {
            if (InsightsCustomDatePanel != null)
                InsightsCustomDatePanel.Visibility = Visibility.Collapsed;
        }

        var summary = _analytics.GenerateSummary(_historyService.Sessions, _taskService.Tasks, preset, customStart, customEnd);

        InsightsFocusHoursText.Text = $"{summary.TotalFocusHours}h";
        InsightsSessionsText.Text = summary.CompletedFocusSessions.ToString();
        InsightsRateText.Text = $"{summary.CompletionRate}%";
        InsightsTasksText.Text = summary.TasksCompleted.ToString();
        InsightsPlannedVsActualText.Text = $"{summary.PlannedMinutes}m / {(int)summary.ActualMinutes}m";

        InsightsProjectsListBox.ItemsSource = summary.ProjectBreakdown;

        DrawInsightsBarChart(summary);
        DrawInsightsHeatmap();
    }

    private void InsightsCustomDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (InsightsCustomDatePanel?.Visibility == Visibility.Visible)
        {
            RefreshInsights();
        }
    }

    private void InsightsResetRangeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (InsightsRangeCombo != null)
            InsightsRangeCombo.SelectedIndex = 0; // Last 7 Days
    }

    private void DrawInsightsBarChart(ProductivitySummary summary)
    {
        InsightsBarChartCanvas.Children.Clear();

        double width = InsightsBarChartCanvas.ActualWidth;
        double height = InsightsBarChartCanvas.ActualHeight;
        if (width <= 10 || height <= 10) return;

        var days = summary.DailyBreakdown;
        if (days.Count == 0) return;

        double maxMins = days.Max(d => d.FocusMinutes);
        if (maxMins < 60) maxMins = 60;

        double barWidth = Math.Max(10, (width / days.Count) - 12);
        double maxBarHeight = height - 34;

        var barBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B5CF6")); // Purple
        var accentBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFB923C")); // Orange
        var textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA78BFA"));

        for (int i = 0; i < days.Count; i++)
        {
            var day = days[i];
            double x = i * (width / days.Count) + ((width / days.Count - barWidth) / 2);
            double barHeight = Math.Max(3, (day.FocusMinutes / maxMins) * maxBarHeight);
            double y = height - 24 - barHeight;

            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                RadiusX = 4,
                RadiusY = 4,
                Fill = day.FocusMinutes >= _settings.DailyFocusGoalMinutes ? accentBrush : barBrush,
                ToolTip = $"{day.Date:MMM d}: {(int)day.FocusMinutes}m focus ({day.CompletedTasks} tasks)"
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            InsightsBarChartCanvas.Children.Add(rect);

            var label = new TextBlock
            {
                Text = day.Date.ToString("MMM d"),
                FontSize = 9,
                Foreground = textBrush,
                Width = width / days.Count,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(label, i * (width / days.Count));
            Canvas.SetTop(label, height - 18);
            InsightsBarChartCanvas.Children.Add(label);

            if (day.FocusMinutes > 0)
            {
                var valLabel = new TextBlock
                {
                    Text = $"{(int)day.FocusMinutes}m",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Width = width / days.Count,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(valLabel, i * (width / days.Count));
                Canvas.SetTop(valLabel, Math.Max(0, y - 14));
                InsightsBarChartCanvas.Children.Add(valLabel);
            }
        }
    }

    private void DrawInsightsHeatmap()
    {
        InsightsHeatmapCanvas.Children.Clear();

        double width = InsightsHeatmapCanvas.ActualWidth;
        double height = InsightsHeatmapCanvas.ActualHeight;
        if (width <= 10 || height <= 10) return;

        var cells = AnalyticsService.GenerateActivityHeatmap(_historyService.Sessions, _taskService.Tasks, weeks: 12);
        if (cells.Count == 0) return;

        int weeks = 12;
        int daysPerWeek = 7;
        double cellMargin = 3;
        double cellSize = Math.Min(13, Math.Min((width - 40) / weeks - cellMargin, (height - 20) / daysPerWeek - cellMargin));
        if (cellSize < 6) cellSize = 6;

        var levelBrushes = new Brush[]
        {
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF282238")), // Level 0 (None)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4C2D6D")), // Level 1 (Light)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7C3AED")), // Level 2 (Medium)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF97316")), // Level 3 (High)
            new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF22C55E"))  // Level 4 (Goal reached)
        };

        // Day of week labels on left (Mon, Wed, Fri)
        string[] dayNames = { "M", "T", "W", "T", "F", "S", "S" };
        for (int d = 0; d < 7; d++)
        {
            if (d == 0 || d == 2 || d == 4)
            {
                var dayLabel = new TextBlock
                {
                    Text = dayNames[d],
                    FontSize = 9,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8D7B9D")),
                    Width = 14,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(dayLabel, 4);
                Canvas.SetTop(dayLabel, d * (cellSize + cellMargin));
                InsightsHeatmapCanvas.Children.Add(dayLabel);
            }
        }

        double startX = 22;
        foreach (var cell in cells)
        {
            double x = startX + cell.WeekIndex * (cellSize + cellMargin);
            double y = cell.DayOfWeekIndex * (cellSize + cellMargin);

            var rect = new Rectangle
            {
                Width = cellSize,
                Height = cellSize,
                RadiusX = 2,
                RadiusY = 2,
                Fill = levelBrushes[Math.Clamp(cell.IntensityLevel, 0, 4)],
                ToolTip = cell.TooltipText
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            InsightsHeatmapCanvas.Children.Add(rect);
        }
    }

    private void InsightsRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshInsights();
    }

    private void InsightsBarChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshInsights();
    }

    private void InsightsHeatmapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawInsightsHeatmap();
    }

    // =========================================================================
    // 6. GOALS VIEW
    // =========================================================================

    private void RefreshGoals()
    {
        if (!_isInitialized) return;
        // Daily Focus Goal
        double todayFocus = _historyService.GetTodayFocusMinutes();
        int dailyGoal = _settings.DailyFocusGoalMinutes;
        int dailyFocusPct = dailyGoal > 0 ? (int)Math.Min(100, Math.Round((todayFocus / dailyGoal) * 100)) : 0;
        GoalsDailyFocusProgressText.Text = $"{(int)todayFocus} / {dailyGoal} mins ({dailyFocusPct}%)";
        GoalsDailyFocusBar.Value = dailyFocusPct;
        GoalsDailyFocusTargetBox.Text = dailyGoal.ToString();

        // Weekly Focus Goal
        var weekSummary = _analytics.GenerateSummary(_historyService.Sessions, _taskService.Tasks, TimeRangePreset.ThisWeek);
        double weekFocus = weekSummary.TotalFocusMinutes;
        int weeklyGoal = _settings.WeeklyFocusGoalMinutes;
        int weeklyFocusPct = weeklyGoal > 0 ? (int)Math.Min(100, Math.Round((weekFocus / weeklyGoal) * 100)) : 0;
        GoalsWeeklyFocusProgressText.Text = $"{(int)weekFocus} / {weeklyGoal} mins ({weeklyFocusPct}%)";
        GoalsWeeklyFocusBar.Value = weeklyFocusPct;
        GoalsWeeklyFocusTargetBox.Text = weeklyGoal.ToString();

        // Daily Task Goal
        int tasksToday = _taskService.Tasks.Count(t => t.IsCompleted && t.CompletedAt.HasValue && TaskItem.ToLocalDate(t.CompletedAt.Value) == DateTime.Today);
        int dailyTaskGoal = _settings.DailyTaskGoal;
        int dailyTaskPct = dailyTaskGoal > 0 ? (int)Math.Min(100, Math.Round(((double)tasksToday / dailyTaskGoal) * 100)) : 0;
        GoalsDailyTaskProgressText.Text = $"{tasksToday} / {dailyTaskGoal} tasks ({dailyTaskPct}%)";
        GoalsDailyTaskBar.Value = dailyTaskPct;
        GoalsDailyTaskTargetBox.Text = dailyTaskGoal.ToString();

        // Weekly Task Goal
        int tasksWeek = weekSummary.TasksCompleted;
        int weeklyTaskGoal = _settings.WeeklyTaskGoal;
        int weeklyTaskPct = weeklyTaskGoal > 0 ? (int)Math.Min(100, Math.Round(((double)tasksWeek / weeklyTaskGoal) * 100)) : 0;
        GoalsWeeklyTaskProgressText.Text = $"{tasksWeek} / {weeklyTaskGoal} tasks ({weeklyTaskPct}%)";
        GoalsWeeklyTaskBar.Value = weeklyTaskPct;
        GoalsWeeklyTaskTargetBox.Text = weeklyTaskGoal.ToString();

        // Streaks
        var (currentStreak, longestStreak) = AnalyticsService.CalculateStreaks(_historyService.Sessions, _taskService.Tasks);
        GoalsStreakBigText.Text = $"{currentStreak} Days 🔥";
        GoalsLongestStreakText.Text = $"  •  Best: {longestStreak} Days";

        if (currentStreak >= 7)
        {
            GoalsCompanionMessageText.Text = "Outstanding! A full week of unstoppable focus! Neku is celebrating with you! 🎉";
        }
        else if (currentStreak >= 3)
        {
            GoalsCompanionMessageText.Text = "Great momentum! Keep the fire burning today!";
        }
        else
        {
            GoalsCompanionMessageText.Text = "Every completed Pomodoro counts. Take it one task at a time!";
        }

        // Goal Achievement History (Last 14 Days)
        var achievements = AnalyticsService.CalculateGoalAchievements(
            _historyService.Sessions,
            _taskService.Tasks,
            _settings,
            daysBack: 14);

        GoalsAchievementItemsControl.ItemsSource = achievements;

        int completedDays = achievements.Count(a => a.BothAchieved);
        int achievementPct = achievements.Count > 0 ? (int)Math.Round((double)completedDays / achievements.Count * 100) : 0;
        GoalsAchievementRateText.Text = $"Achievement Rate: {achievementPct}% ({completedDays}/{achievements.Count} days)";
    }

    private void GoalsTarget_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(GoalsDailyFocusTargetBox.Text, out int df) && df > 0)
            _settings.DailyFocusGoalMinutes = df;
        if (int.TryParse(GoalsWeeklyFocusTargetBox.Text, out int wf) && wf > 0)
            _settings.WeeklyFocusGoalMinutes = wf;
        if (int.TryParse(GoalsDailyTaskTargetBox.Text, out int dt) && dt > 0)
            _settings.DailyTaskGoal = dt;
        if (int.TryParse(GoalsWeeklyTaskTargetBox.Text, out int wt) && wt > 0)
            _settings.WeeklyTaskGoal = wt;

        RefreshGoals();
    }

    // =========================================================================
    // KEYBOARD SHORTCUTS
    // =========================================================================

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        HandleKeyDown(e.Key, Keyboard.Modifiers, e);
    }

    internal void HandleKeyDown(Key key, ModifierKeys modifiers, KeyEventArgs? e = null)
    {
        // 0. Command Palette shortcut (Ctrl+P, Ctrl+Shift+P, Ctrl+Alt+P)
        if (key == Key.P && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            App.CurrentApp?.ToggleCommandPalette();
            if (e != null) e.Handled = true;
            return;
        }

        // 1. Ctrl + Number (Tab switching Ctrl+1 to Ctrl+8)
        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.D1 or Key.NumPad1:
                    SelectTab("Overview");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D2 or Key.NumPad2:
                    SelectTab("Tasks");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D3 or Key.NumPad3:
                    SelectTab("Focus");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D4 or Key.NumPad4:
                    SelectTab("History");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D5 or Key.NumPad5:
                    SelectTab("Insights");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D6 or Key.NumPad6:
                    SelectTab("Goals");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D7 or Key.NumPad7:
                    SelectTab("Agent");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D8 or Key.NumPad8:
                    SelectTab("Radar");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D9 or Key.NumPad9:
                    SelectTab("Health");
                    if (e != null) e.Handled = true;
                    return;
                case Key.N:
                    SelectTab("Tasks");
                    TaskAddNewBtn_Click(this, new RoutedEventArgs());
                    if (e != null) e.Handled = true;
                    return;
                case Key.F:
                    if (_currentTab == "History")
                    {
                        HistoryNotesTextBox.Focus();
                        HistoryNotesTextBox.SelectAll();
                    }
                    else
                    {
                        SelectTab("Tasks");
                        TaskSearchBox.Focus();
                        TaskSearchBox.SelectAll();
                    }
                    if (e != null) e.Handled = true;
                    return;
                case Key.Enter:
                    if (_currentTab == "Tasks" && _selectedTask != null)
                    {
                        DetailStartFocusBtn_Click(this, new RoutedEventArgs());
                        if (e != null) e.Handled = true;
                        return;
                    }
                    break;
                case Key.W:
                    Close();
                    if (e != null) e.Handled = true;
                    return;
                case Key.S:
                    if (_currentTab == "History" || HistoryNotesTextBox.IsKeyboardFocusWithin)
                    {
                        HistorySaveNotesBtn_Click(this, new RoutedEventArgs());
                        if (e != null) e.Handled = true;
                        return;
                    }
                    break;
                case Key.R:
                    SelectTab(_currentTab);
                    if (e != null) e.Handled = true;
                    return;
            }
        }

        // F5 refresh for active tab
        if (key == Key.F5 && modifiers == ModifierKeys.None)
        {
            SelectTab(_currentTab);
            if (e != null) e.Handled = true;
            return;
        }

        // 2. Ctrl+Tab / Ctrl+Shift+Tab (Tab cycling)
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control && key == Key.Tab)
        {
            string[] tabs = ["Overview", "Tasks", "Focus", "History", "Insights", "Goals", "Agent", "Radar", "Health"];
            int currentIndex = Array.IndexOf(tabs, _currentTab == "AgentActivity" ? "Agent" : _currentTab);
            if (currentIndex < 0) currentIndex = 0;

            bool reverse = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            int nextIndex = reverse
                ? (currentIndex - 1 + tabs.Length) % tabs.Length
                : (currentIndex + 1) % tabs.Length;

            SelectTab(tabs[nextIndex]);
            if (e != null) e.Handled = true;
            return;
        }

        // 3. Task list shortcuts when TasksListBox is focused
        if (TasksListBox.IsKeyboardFocusWithin && _selectedTask != null)
        {
            if (key == Key.Space && modifiers == ModifierKeys.None)
            {
                _taskService.ToggleComplete(_selectedTask.Id);
                RefreshTaskList();
                if (e != null) e.Handled = true;
                return;
            }
            if (key == Key.Delete && modifiers == ModifierKeys.None)
            {
                DetailDeleteBtn_Click(this, new RoutedEventArgs());
                if (e != null) e.Handled = true;
                return;
            }
        }

        // 4. Escape handling
        else if (key == Key.Escape)
        {
            if (AgentSetupModal.Visibility == Visibility.Visible)
            {
                AgentSetupModal.Visibility = Visibility.Collapsed;
                if (e != null) e.Handled = true;
                return;
            }

            if (TaskSearchBox.IsKeyboardFocusWithin && !string.IsNullOrEmpty(TaskSearchBox.Text))
            {
                TaskSearchBox.Text = "";
                if (e != null) e.Handled = true;
                return;
            }

            // If a TextBox has focus, unfocus it first instead of immediately closing window or wiping content
            if (FocusManager.GetFocusedElement(this) is TextBox || HistoryNotesTextBox.IsKeyboardFocusWithin)
            {
                Keyboard.ClearFocus();
                if (e != null) e.Handled = true;
                return;
            }

            Close();
            if (e != null) e.Handled = true;
        }
    }

    // =========================================================================
    // 7. AGENT ACTIVITY VIEW
    // =========================================================================

    private void OnAgentHistoryChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_currentTab is "Agent" or "AgentActivity")
                RefreshAgentActivity();
        });
    }

    private void OnAgentSessionUpdated(AgentSession session)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_currentTab is "Agent" or "AgentActivity")
            {
                RefreshAgentActivity();
                if (_selectedAgentSession?.SessionId == session.SessionId)
                {
                    DisplayAgentSessionDetails(session);
                }
            }
        });
    }

    public void RefreshAgentActivity()
    {
        if (_agentActivityService == null) return;

        // Status badge
        bool isListening = _settings.EnableAgentCompanion || _settings.EnableDeveloperEvents;
        AgentStatusBadgeText.Text = isListening ? $"● Listening ({_settings.DeveloperPort})" : "○ Disabled";
        AgentStatusBadgeText.Foreground = isListening
            ? (Brush)FindResource("AccentGreenBrush")
            : (Brush)FindResource("TextDimBrush");

        // Filter counts
        var allSessions = _agentActivityService.GetSessions("all");
        int activeCount = allSessions.Count(s => s.Status == AgentSessionStatus.Active);
        int waitingCount = allSessions.Count(s => s.Status == AgentSessionStatus.WaitingForInput);
        int completedCount = allSessions.Count(s => s.Status == AgentSessionStatus.Completed);
        int failedCount = allSessions.Count(s => s.Status == AgentSessionStatus.Failed);

        AgentFilterAllBtn.Content = $"All ({allSessions.Count})";
        AgentFilterActiveBtn.Content = $"Active ({activeCount})";
        AgentFilterWaitingBtn.Content = $"Waiting ({waitingCount})";
        AgentFilterCompletedBtn.Content = $"Completed ({completedCount})";
        AgentFilterFailedBtn.Content = $"Failed ({failedCount})";
        AgentFilterSummaryText.Text = $"{activeCount} active, {waitingCount} waiting, {allSessions.Count} total";

        var filtered = _agentActivityService.GetSessions(_currentAgentFilter);
        AgentSessionListBox.ItemsSource = filtered;

        if (filtered.Count == 0)
        {
            AgentNoSessionsPanel.Visibility = Visibility.Visible;
            AgentSessionDetailPanel.Visibility = Visibility.Collapsed;
            AgentNoSessionSelectedPanel.Visibility = Visibility.Collapsed;
            _selectedAgentSession = null;
        }
        else
        {
            AgentNoSessionsPanel.Visibility = Visibility.Collapsed;

            // Retain selection or select first
            if (_selectedAgentSession == null || !filtered.Any(s => s.SessionId == _selectedAgentSession.SessionId))
            {
                _selectedAgentSession = filtered.FirstOrDefault();
            }
            else
            {
                _selectedAgentSession = filtered.FirstOrDefault(s => s.SessionId == _selectedAgentSession.SessionId) ?? filtered.FirstOrDefault();
            }

            AgentSessionListBox.SelectedItem = _selectedAgentSession;
            DisplayAgentSessionDetails(_selectedAgentSession);
        }
    }

    private void DisplayAgentSessionDetails(AgentSession? session)
    {
        if (session == null)
        {
            AgentSessionDetailPanel.Visibility = Visibility.Collapsed;
            AgentNoSessionSelectedPanel.Visibility = Visibility.Visible;
            return;
        }

        AgentSessionDetailPanel.Visibility = Visibility.Visible;
        AgentNoSessionSelectedPanel.Visibility = Visibility.Collapsed;

        AgentDetailProjectText.Text = session.ProjectName;
        AgentDetailProviderText.Text = session.ProviderName;
        AgentDetailPathText.Text = session.RedactedWorkingDirectory;
        AgentDetailIdText.Text = $"Session ID: {session.SessionId}";
        AgentDetailDurationText.Text = $"Duration: {session.DurationText}";

        // Safe approval boundary banner
        AgentWaitingBanner.Visibility = session.Status == AgentSessionStatus.WaitingForInput
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Events list in chronological (most recent first) order
        var eventsList = session.Events.OrderByDescending(e => e.Timestamp).ToList();
        AgentEventTimelineItemsControl.ItemsSource = eventsList;
        AgentTimelineCountText.Text = $"{eventsList.Count} events";
    }

    private void AgentSessionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AgentSessionListBox.SelectedItem is AgentSession session)
        {
            _selectedAgentSession = session;
            DisplayAgentSessionDetails(session);
        }
    }

    private void AgentFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _currentAgentFilter = tag;

            // Update filter button styles
            AgentFilterAllBtn.Style = (Style)FindResource(tag == "all" ? "PrimaryButton" : "SecondaryButton");
            AgentFilterActiveBtn.Style = (Style)FindResource(tag == "active" ? "PrimaryButton" : "SecondaryButton");
            AgentFilterWaitingBtn.Style = (Style)FindResource(tag == "waiting" ? "PrimaryButton" : "SecondaryButton");
            AgentFilterCompletedBtn.Style = (Style)FindResource(tag == "completed" ? "PrimaryButton" : "SecondaryButton");
            AgentFilterFailedBtn.Style = (Style)FindResource(tag == "failed" ? "PrimaryButton" : "SecondaryButton");

            RefreshAgentActivity();
        }
    }

    private void AgentClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        int cleared = _agentActivityService?.ClearCompletedHistory() ?? 0;
        RefreshAgentActivity();
    }

    private void AgentRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        _agentActivityService?.CheckIdleSessions();
        RefreshAgentActivity();
    }

    private void AgentOpenFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAgentSession != null && !string.IsNullOrWhiteSpace(_selectedAgentSession.WorkingDirectory))
        {
            string resolved = AgentSession.ResolvePath(_selectedAgentSession.WorkingDirectory);
            if (Directory.Exists(resolved))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = resolved,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open directory: {ex.Message}", "Open Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    private void AgentConfigureClaudeBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenClaudeCodeSetupModal();
    }

    private void OpenClaudeCodeSetupModal()
    {
        bool detected = _claudeCodeService.IsClaudeDetected(out string detectedPath);
        string settingsPath = _claudeCodeService.GetSettingsPath();

        AgentClaudeDetectedText.Text = detected
            ? $"✓ Detected: {detectedPath}"
            : "⚠️ Claude Code not found in standard paths (hooks can still be configured)";
        AgentClaudeDetectedText.Foreground = detected
            ? (Brush)FindResource("AccentGreenBrush")
            : (Brush)FindResource("AccentOrangeBrush");

        AgentClaudeSettingsPathText.Text = $"Path: {settingsPath}";

        bool isInstalled = _claudeCodeService.IsHookInstalled();
        string currentContent = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : "{}";
        string proposedContent = _claudeCodeService.GenerateProposedJson(currentContent, !isInstalled, _settings.DeveloperPort);
        string diff = _claudeCodeService.GenerateDiff(currentContent, proposedContent);

        AgentDiffTextBox.Text = diff;
        AgentApplyHooksBtn.Content = isInstalled ? "Update Hooks" : "✓ Apply & Install Hooks";
        AgentUninstallHooksBtn.Visibility = isInstalled ? Visibility.Visible : Visibility.Collapsed;

        AgentSetupModal.Visibility = Visibility.Visible;
    }

    private void AgentCloseSetupModal_Click(object sender, RoutedEventArgs e)
    {
        AgentSetupModal.Visibility = Visibility.Collapsed;
    }

    private void AgentApplyHooksBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = _claudeCodeService.Apply(enable: true, port: _settings.DeveloperPort);
        if (result.Success)
        {
            MessageBox.Show(result.Message, "Claude Code Hooks", MessageBoxButton.OK, MessageBoxImage.Information);
            AgentSetupModal.Visibility = Visibility.Collapsed;
            RefreshAgentActivity();
        }
        else
        {
            MessageBox.Show(result.Message, "Error Applying Hooks", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AgentUninstallHooksBtn_Click(object sender, RoutedEventArgs e)
    {
        var result = _claudeCodeService.Apply(enable: false, port: _settings.DeveloperPort);
        if (result.Success)
        {
            MessageBox.Show(result.Message, "Claude Code Hooks", MessageBoxButton.OK, MessageBoxImage.Information);
            AgentSetupModal.Visibility = Visibility.Collapsed;
            RefreshAgentActivity();
        }
        else
        {
            MessageBox.Show(result.Message, "Error Uninstalling Hooks", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================================================================
    // DEVELOPER RADAR
    // =========================================================================

    private void OnRadarStateChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_currentTab == "Radar")
            {
                RefreshRadar();
            }
        });
    }

    private void RefreshRadar()
    {
        if (_radarService == null) return;

        // Context Badge
        var ctx = _radarService.CurrentContext;
        string ctxLabel = ctx.IsPresentationOrMeeting ? "🎥 Meeting / Presenting" :
                          ctx.IsIdle ? "💤 Idle" :
                          ctx.Category switch
                          {
                              DeskSenseAppCategory.Coding => $"💻 Coding: {ctx.ProcessName}",
                              DeskSenseAppCategory.Terminal => $"⚡ Terminal: {ctx.ProcessName}",
                              DeskSenseAppCategory.Browser => $"🌐 Browser: {ctx.ProcessName}",
                              DeskSenseAppCategory.Meeting => $"💬 Meeting: {ctx.ProcessName}",
                              _ => $"🖥️ {ctx.ProcessName}"
                          };

        RadarContextText.Text = ctxLabel;

        // Active App Card
        RadarActiveAppText.Text = string.IsNullOrWhiteSpace(ctx.ProcessName) ? "Desktop" : ctx.ProcessName;
        RadarActiveAppSubtext.Text = $"Category: {ctx.Category} • {ctx.DurationDescription}";

        // Selected Repository & Active Branch Card
        var selectedRepo = _radarService.GetSelectedRepository();
        if (selectedRepo != null)
        {
            RadarBranchText.Text = selectedRepo.DisplayStatus;
            RadarBranchText.Foreground = selectedRepo.StatusBrush;
            RadarRepoSubtext.Text = selectedRepo.Path;
        }
        else
        {
            RadarBranchText.Text = "No repository monitored";
            RadarBranchText.Foreground = (Brush)FindResource("TextDimBrush");
            RadarRepoSubtext.Text = "Click + Add Repo to track git status";
        }

        // Latest Build / Test Card
        var latestBuild = _radarService.GetLatestBuildRecord();
        if (latestBuild != null)
        {
            RadarBuildStatusText.Text = $"{latestBuild.StatusBadgeText} - {latestBuild.ProjectName}";
            RadarBuildStatusText.Foreground = latestBuild.StatusBrush;
            RadarBuildSubtext.Text = $"{latestBuild.Message} ({latestBuild.FormattedTime})";
        }
        else
        {
            RadarBuildStatusText.Text = "— No build events";
            RadarBuildStatusText.Foreground = (Brush)FindResource("TextDimBrush");
            RadarBuildSubtext.Text = "Awaiting build/test webhook";
        }

        // Monitoring State & Warning Banner
        RadarPauseBtn.Content = _radarService.RepoMonitor.IsPaused ? "▶ Resume" : "⏸ Pause";
        RadarGitWarningBanner.Visibility = _radarService.RepoMonitor.IsGitAvailable ? Visibility.Collapsed : Visibility.Visible;

        // Repositories List
        var repos = _radarService.RepoMonitor.GetRepositories();
        RadarRepoListBox.ItemsSource = null;
        RadarRepoListBox.ItemsSource = repos;
        if (selectedRepo != null)
        {
            RadarRepoListBox.SelectedItem = repos.FirstOrDefault(r => string.Equals(r.Path, selectedRepo.Path, StringComparison.OrdinalIgnoreCase));
        }

        // Build Events List
        var builds = _radarService.GetBuildRecords();
        RadarBuildListBox.ItemsSource = null;
        RadarBuildListBox.ItemsSource = builds;
    }

    private void RadarRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_radarService != null)
        {
            _ = _radarService.RepoMonitor.RefreshAllAsync();
            RefreshRadar();
        }
    }

    private void RadarPauseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_radarService != null)
        {
            _radarService.RepoMonitor.IsPaused = !_radarService.RepoMonitor.IsPaused;
            RefreshRadar();
        }
    }

    private void RadarHudToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _onToggleHud?.Invoke();
    }

    private void RadarAddRepoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_radarService == null) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Git Repository Folder",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            _radarService.RepoMonitor.AddRepository(dialog.FolderName);
            _radarService.SelectedRepositoryPath = dialog.FolderName;
            RefreshRadar();
        }
    }

    private void RadarRemoveRepoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_radarService == null) return;

        var selected = RadarRepoListBox.SelectedItem as MonitoredRepository ?? _radarService.GetSelectedRepository();
        if (selected != null)
        {
            _radarService.RepoMonitor.RemoveRepository(selected.Path);
            RefreshRadar();
        }
    }

    private void RadarOpenExplorerBtn_Click(object sender, RoutedEventArgs e)
    {
        _radarService?.OpenInExplorer();
    }

    private void RadarCopyPathBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_radarService != null && _radarService.CopyPathToClipboard())
        {
            RadarCopyPathBtn.Content = "✓ Copied";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                RadarCopyPathBtn.Content = "📋 Copy Path";
                timer.Stop();
            };
            timer.Start();
        }
    }

    private void RadarRepoListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RadarRepoListBox.SelectedItem is MonitoredRepository repo && _radarService != null)
        {
            _radarService.SelectedRepositoryPath = repo.Path;
            RadarBranchText.Text = repo.DisplayStatus;
            RadarBranchText.Foreground = repo.StatusBrush;
            RadarRepoSubtext.Text = repo.Path;
        }
    }

    // =========================================================================
    // 9. SYSTEM HEALTH VIEW
    // =========================================================================

    private void RefreshHealth()
    {
        if (_healthService == null) return;
        var snap = _healthService.CurrentSnapshot;

        // CPU
        HealthCpuPercentText.Text = $"{snap.CpuPercent}%";
        HealthCpuProgressBar.Value = snap.CpuPercent;
        HealthCpuStatusText.Text = snap.IsCpuCritical ? "High Load" : "Normal";
        HealthCpuStatusText.Foreground = snap.IsCpuCritical
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF87171"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF22C55E"));
        HealthCpuThresholdText.Text = $"Alert threshold: {_settings.SystemHealthCpuThreshold}%";

        // RAM
        HealthRamPercentText.Text = $"{snap.MemoryPercent}%";
        HealthRamProgressBar.Value = snap.MemoryPercent;
        HealthRamDetailText.Text = $"{snap.UsedMemoryGb} / {snap.TotalMemoryGb} GB used";
        HealthRamStatusText.Text = snap.IsMemoryCritical ? "Pressure" : "Normal";
        HealthRamStatusText.Foreground = snap.IsMemoryCritical
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF87171"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF22C55E"));

        // Storage
        var primaryDrive = snap.Drives.FirstOrDefault();
        if (primaryDrive != null)
        {
            HealthDiskFreeText.Text = $"{primaryDrive.FreeGb} GB";
            HealthDiskProgressBar.Value = primaryDrive.UsedPercent;
            HealthDiskDetailText.Text = $"{primaryDrive.Name} ({primaryDrive.UsedPercent}% used)";
            HealthDiskStatusText.Text = snap.IsDiskCritical ? "Low Space" : "Healthy";
            HealthDiskStatusText.Foreground = snap.IsDiskCritical
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF87171"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF22C55E"));
        }
        else
        {
            HealthDiskFreeText.Text = "N/A";
            HealthDiskProgressBar.Value = 0;
            HealthDiskDetailText.Text = "No drives detected";
            HealthDiskStatusText.Text = "Normal";
        }

        // Battery / Power
        HealthBatteryPercentText.Text = snap.HasBattery ? $"{snap.BatteryPercent}%" : "AC";
        HealthBatteryProgressBar.Value = snap.HasBattery ? Math.Max(0, snap.BatteryPercent) : 100;
        HealthBatteryDetailText.Text = snap.HasBattery
            ? (snap.IsCharging ? "Charging" : "On Battery")
            : "Connected to Power";
        HealthPowerStatusText.Text = snap.IsBatteryCritical ? "Low Battery" : (snap.IsCharging ? "Charging" : "AC");
        HealthPowerStatusText.Foreground = snap.IsBatteryCritical
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF87171"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF22C55E"));

        // Drives list
        HealthDrivesList.ItemsSource = snap.Drives;

        // Processes list
        HealthProcessesList.ItemsSource = snap.Processes;
        HealthProcessCountText.Text = $"{snap.Processes.Count} active";
    }

    private void HealthRefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        _healthService?.Poll();
        RefreshHealth();
    }
}

public sealed class TodayTaskDisplayItem
{
    public TaskItem Task { get; }
    public string Title => Task.Title;
    public bool IsCompleted => Task.IsCompleted;
    public string PriorityDisplay => Task.Priority switch
    {
        TaskPriority.High => "● High",
        TaskPriority.Low => "● Low",
        _ => "● Normal"
    };
    public Brush PriorityColor => Task.Priority switch
    {
        TaskPriority.High => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        TaskPriority.Low => new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
        _ => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24))
    };
    public string ProjectDisplay => string.IsNullOrEmpty(Task.Project) ? "" : $"[{Task.Project}]";

    public TodayTaskDisplayItem(TaskItem task)
    {
        Task = task;
    }
}

public sealed class NoteOverviewItem
{
    public NoteItem Note { get; }
    public string Title => string.IsNullOrWhiteSpace(Note.Title) ? "Untitled" : Note.Title;
    public string Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Note.Content)) return "Empty note";
            string firstLine = Note.Content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return firstLine.Length > 40 ? firstLine[..40] + "…" : firstLine;
        }
    }
    public string FormattedDate => Note.UpdatedAt.ToLocalTime().ToString("MMM d");

    public NoteOverviewItem(NoteItem note)
    {
        Note = note;
    }
}
