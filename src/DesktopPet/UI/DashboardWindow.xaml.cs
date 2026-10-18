using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DesktopPet.Services;
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

    private string _currentTab = "Overview";
    private string _currentTaskFilterTab = "All";
    private TaskItem? _selectedTask;
    private bool _suppressTaskEvents = false;
    private bool _isInitialized = false;
    private DispatcherTimer? _uiRefreshTimer;

    public event Action<TaskItem>? TaskFocusRequested;

    public DashboardWindow(
        TaskService taskService,
        FocusTimerService focusTimer,
        FocusHistoryService historyService,
        AppSettings settings,
        string? initialTab = null)
    {
        _taskService = taskService;
        _focusTimer = focusTimer;
        _historyService = historyService;
        _settings = settings;

        InitializeComponent();
        _isInitialized = true;

        // Wire event listeners
        _taskService.TasksChanged += OnTasksChanged;
        _focusTimer.StateChanged += OnTimerStateChanged;
        _historyService.HistoryChanged += OnHistoryChanged;

        // Ticking timer for smooth clock countdowns
        _uiRefreshTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _uiRefreshTimer.Tick += (_, _) => UpdateTimerDisplays();
        _uiRefreshTimer.Start();

        SelectTab(initialTab ?? "Overview");
        UpdateTimerDisplays();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _taskService.TasksChanged -= OnTasksChanged;
        _focusTimer.StateChanged -= OnTimerStateChanged;
        _historyService.HistoryChanged -= OnHistoryChanged;
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

        // Update nav button styles
        UpdateNavButton(NavOverviewBtn, tabName == "Overview");
        UpdateNavButton(NavTasksBtn, tabName == "Tasks");
        UpdateNavButton(NavFocusBtn, tabName == "Focus");
        UpdateNavButton(NavHistoryBtn, tabName == "History");
        UpdateNavButton(NavInsightsBtn, tabName == "Insights");
        UpdateNavButton(NavGoalsBtn, tabName == "Goals");

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
        }
    }

    private void UpdateNavButton(Button btn, bool isActive)
    {
        var activeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8B5CF6"));
        var activeFg = Brushes.White;
        var inactiveFg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA78BFA"));

        btn.Background = isActive ? activeBg : Brushes.Transparent;
        btn.Foreground = isActive ? activeFg : inactiveFg;
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
            badgeColor = (Color)ColorConverter.ConvertFromString("#FFFB923C"); // Orange
            RailPlayPauseBtn.Content = "⏸ Pause";
        }
        else if (_focusTimer.State == FocusTimerState.FocusPaused)
        {
            badgeText = "PAUSED";
            badgeColor = (Color)ColorConverter.ConvertFromString("#FFFBBF24"); // Amber
            RailPlayPauseBtn.Content = "▶ Resume";
        }
        else if (_focusTimer.State == FocusTimerState.Break)
        {
            badgeText = _focusTimer.IsLongBreak ? "LONG BREAK" : "BREAK";
            badgeColor = (Color)ColorConverter.ConvertFromString("#FF34D399"); // Green
            RailPlayPauseBtn.Content = "⏸ Pause";
        }
        else if (_focusTimer.State == FocusTimerState.BreakPaused)
        {
            badgeText = "BREAK PAUSED";
            badgeColor = (Color)ColorConverter.ConvertFromString("#FFFBBF24"); // Amber
            RailPlayPauseBtn.Content = "▶ Resume";
        }
        else
        {
            badgeText = "IDLE";
            badgeColor = (Color)ColorConverter.ConvertFromString("#FFA78BFA"); // Muted purple
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

        // Recent sessions list (last 5)
        OverviewRecentListBox.ItemsSource = allSessions.Take(5).ToList();

        DrawWeeklyChart();
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
    }

    private void TaskTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabTag)
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
        _settings.EnableUpcomingTaskReminders   = FocusNotifyUpcomingCheck.IsChecked ?? true;
        _settings.EnableOverdueTaskReminders    = FocusNotifyOverdueCheck.IsChecked ?? true;
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
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.N)
            {
                SelectTab("Tasks");
                TaskAddNewBtn_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F)
            {
                SelectTab("Tasks");
                TaskSearchBox.Focus();
                TaskSearchBox.SelectAll();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
