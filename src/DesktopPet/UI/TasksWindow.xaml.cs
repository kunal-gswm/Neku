using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopPet.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace DesktopPet.UI;

public partial class TasksWindow : Window
{
    private readonly TaskService _taskService;
    private readonly FocusTimerService _focusTimer;
    private readonly AppSettings _settings;
    private TaskItem? _selectedTask;
    private string _activeTab = "All";
    public string ActiveTab => _activeTab;
    private bool _suppressEditEvents;
    private bool _isInitialized;

    public event Action<TaskItem>? TaskFocusRequested;
    public event Action? OpenHubRequested;

    private void OpenHubBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenHubRequested?.Invoke();
    }

    public TasksWindow(TaskService taskService, FocusTimerService focusTimer, AppSettings settings)
    {
        _taskService = taskService;
        _focusTimer = focusTimer;
        _settings = settings;

        InitializeComponent();
        _isInitialized = true;

        _taskService.TasksChanged += OnTasksChangedFromService;

        RefreshList();
        UpdateTabButtons();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        DesktopPet.Native.Win32.EnableImmersiveDarkMode(handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _taskService.TasksChanged -= OnTasksChangedFromService;
    }

    private void OnTasksChangedFromService()
    {
        Dispatcher.InvokeAsync(() =>
        {
            RefreshList();
            UpdateTabCounts();
        });
    }

    private void RefreshList()
    {
        var allTasks = _taskService.Tasks;

        // 1. Filter by Active Tab
        IEnumerable<TaskItem> filtered = _activeTab switch
        {
            "Today" => allTasks.Where(t => !t.IsCompleted && t.DueDate.HasValue && TaskItem.ToLocalDate(t.DueDate.Value) <= DateTime.Today),
            "Upcoming" => allTasks.Where(t => !t.IsCompleted && t.DueDate.HasValue && TaskItem.ToLocalDate(t.DueDate.Value) > DateTime.Today),
            "Completed" => allTasks.Where(t => t.IsCompleted),
            _ => allTasks.Where(t => !t.IsCompleted) // "All" shows all pending tasks
        };

        // 2. Filter by Search Query
        string query = SearchBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Sort
        string sortChoice = (SortCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Due Date";
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

        // Update empty state
        if (list.Count == 0)
        {
            EmptyStateBorder.Visibility = Visibility.Visible;
            TasksListBox.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrEmpty(query))
            {
                EmptyStateIcon.Text = "🔍";
                EmptyStateTitle.Text = "No matching tasks found";
                EmptyStateSubtitle.Text = "Try a different search term";
            }
            else if (_activeTab == "Today")
            {
                EmptyStateIcon.Text = "☀️";
                EmptyStateTitle.Text = "All caught up for today!";
                EmptyStateSubtitle.Text = "No pending tasks due today";
            }
            else if (_activeTab == "Upcoming")
            {
                EmptyStateIcon.Text = "📅";
                EmptyStateTitle.Text = "No upcoming tasks scheduled";
                EmptyStateSubtitle.Text = "Set a due date to see tasks here";
            }
            else if (_activeTab == "Completed")
            {
                EmptyStateIcon.Text = "✅";
                EmptyStateTitle.Text = "No completed tasks yet";
                EmptyStateSubtitle.Text = "Check off a task to celebrate completion!";
            }
            else
            {
                EmptyStateIcon.Text = "📝";
                EmptyStateTitle.Text = "No active tasks";
                EmptyStateSubtitle.Text = "Click + New Task (Ctrl+N) to get started";
            }
        }
        else
        {
            EmptyStateBorder.Visibility = Visibility.Collapsed;
            TasksListBox.Visibility = Visibility.Visible;
        }

        // Maintain or clear selection
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

        UpdateTabCounts();
    }

    private void UpdateTabCounts()
    {
        var all = _taskService.Tasks;
        int todayCount = all.Count(t => !t.IsCompleted && t.DueDate.HasValue && TaskItem.ToLocalDate(t.DueDate.Value) <= DateTime.Today);
        int upcomingCount = all.Count(t => !t.IsCompleted && t.DueDate.HasValue && TaskItem.ToLocalDate(t.DueDate.Value) > DateTime.Today);
        int allPendingCount = all.Count(t => !t.IsCompleted);
        int completedCount = all.Count(t => t.IsCompleted);

        TodayTabBtn.Content = todayCount > 0 ? $"☀️ Today ({todayCount})" : "☀️ Today";
        UpcomingTabBtn.Content = upcomingCount > 0 ? $"📅 Upcoming ({upcomingCount})" : "📅 Upcoming";
        AllTabBtn.Content = allPendingCount > 0 ? $"📋 All ({allPendingCount})" : "📋 All";
        CompletedTabBtn.Content = completedCount > 0 ? $"✅ Done ({completedCount})" : "✅ Done";
    }

    private static readonly SolidColorBrush ActiveTabBgBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0x45, 0x2C, 0x63));
    private static readonly SolidColorBrush ActiveTabBorderBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0x8B, 0x5C, 0xF6));
    private static readonly SolidColorBrush NormalTabBgBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0x22, 0x1B, 0x30));
    private static readonly SolidColorBrush NormalTabBorderBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0x38, 0x2F, 0x4A));
    private static readonly SolidColorBrush NormalTabFgBrush = CreateFrozenBrush(Color.FromArgb(0xFF, 0xA7, 0x8B, 0xFA));

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void UpdateTabButtons()
    {
        ApplyTabStyle(TodayTabBtn, _activeTab == "Today");
        ApplyTabStyle(UpcomingTabBtn, _activeTab == "Upcoming");
        ApplyTabStyle(AllTabBtn, _activeTab == "All");
        ApplyTabStyle(CompletedTabBtn, _activeTab == "Completed");

        void ApplyTabStyle(Button btn, bool isActive)
        {
            btn.Background = isActive ? ActiveTabBgBrush : NormalTabBgBrush;
            btn.BorderBrush = isActive ? ActiveTabBorderBrush : NormalTabBorderBrush;
            btn.Foreground = isActive ? Brushes.White : NormalTabFgBrush;
            btn.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    public void SelectTab(string tabName)
    {
        _activeTab = tabName;
        UpdateTabButtons();
        RefreshList();
    }

    private void TabBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabName)
        {
            SelectTab(tabName);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized) return;
        RefreshList();
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        RefreshList();
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
            DetailPanel.Visibility = Visibility.Collapsed;
            NoSelectionBorder.Visibility = Visibility.Visible;
            return;
        }

        _suppressEditEvents = true;

        DetailCompleteCheck.IsChecked = task.IsCompleted;
        DetailCompleteCheck.Content = task.IsCompleted ? "Completed ✅" : "Mark Completed";

        if (!TitleBox.IsKeyboardFocusWithin)
        {
            TitleBox.Text = task.Title;
        }
        if (!DescriptionBox.IsKeyboardFocusWithin)
        {
            DescriptionBox.Text = task.Description;
        }

        // Priority
        switch (task.Priority)
        {
            case TaskPriority.Urgent:
                PriorityCombo.SelectedIndex = 3;
                break;
            case TaskPriority.High:
                PriorityCombo.SelectedIndex = 2;
                break;
            case TaskPriority.Low:
                PriorityCombo.SelectedIndex = 0;
                break;
            default:
                PriorityCombo.SelectedIndex = 1;
                break;
        }

        // Due Date
        DueDatePicker.SelectedDate = task.DueDate;

        // Estimate
        switch (task.EstimatedMinutes)
        {
            case 25:
                EstMinutesCombo.SelectedIndex = 1;
                break;
            case 50:
                EstMinutesCombo.SelectedIndex = 2;
                break;
            case 75:
                EstMinutesCombo.SelectedIndex = 3;
                break;
            case 100:
                EstMinutesCombo.SelectedIndex = 4;
                break;
            default:
                EstMinutesCombo.SelectedIndex = 0;
                break;
        }

        // Focus and Pomodoro stats
        PomodoroStatsText.Text = task.CompletedPomodoros > 0
            ? $"🍅 {task.CompletedPomodoros} focus {(task.CompletedPomodoros == 1 ? "session" : "sessions")} completed on this task"
            : "🍅 0 focus sessions completed on this task";

        StartFocusBtn.Content = $"🎯 Start Focus ({_settings.FocusDurationMinutes}m)";

        _suppressEditEvents = false;

        DetailPanel.Visibility = Visibility.Visible;
        NoSelectionBorder.Visibility = Visibility.Collapsed;
    }

    /// <summary>Creates a new task and focuses its title field for immediate editing.</summary>
    public void CreateNewTask()
    {
        NewTaskBtn_Click(this, new RoutedEventArgs());
    }

    private void QuickTaskBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (QuickTaskBox.Text == "Add a task quickly... (press Enter)")
        {
            QuickTaskBox.Text = "";
        }
    }

    private void QuickTaskBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(QuickTaskBox.Text))
        {
            QuickTaskBox.Text = "Add a task quickly... (press Enter)";
        }
    }

    private void QuickTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateFromQuickBox();
            e.Handled = true;
        }
    }

    private void QuickAddBtn_Click(object sender, RoutedEventArgs e)
    {
        CreateFromQuickBox();
    }

    private void CreateFromQuickBox()
    {
        string title = QuickTaskBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(title) || title == "Add a task quickly... (press Enter)") return;

        DateTime? defaultDue = _activeTab == "Today" ? DateTime.Today : (_activeTab == "Upcoming" ? DateTime.Today.AddDays(1) : (DateTime?)null);
        var task = _taskService.Create(
            title: title,
            dueDate: defaultDue,
            estimatedMinutes: 25);

        QuickTaskBox.Text = "";
        _selectedTask = task;
        RefreshList();
    }

    private void NewTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        DateTime? defaultDue = _activeTab == "Today" ? DateTime.Today : (_activeTab == "Upcoming" ? DateTime.Today.AddDays(1) : (DateTime?)null);
        var newTask = _taskService.Create(
            title: "New Task",
            description: "",
            priority: TaskPriority.Normal,
            dueDate: defaultDue,
            estimatedMinutes: 25);

        _selectedTask = newTask;
        RefreshList();
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void TaskItemCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is TaskItem task)
        {
            _taskService.ToggleComplete(task.Id);
            RefreshList();
        }
    }

    private void DetailCompleteCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressEditEvents || _selectedTask == null) return;
        _taskService.ToggleComplete(_selectedTask.Id);
        RefreshList();
    }

    private void StartFocusBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;

        if (TaskFocusRequested != null)
        {
            TaskFocusRequested.Invoke(_selectedTask);
        }
        else
        {
            _focusTimer.StartFocus(_settings.FocusDurationMinutes, _selectedTask.Id, _selectedTask.Title);
        }
    }

    private void DeleteTaskBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete '{_selectedTask.Title}'?",
            "Delete Task",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            string idToDelete = _selectedTask.Id;
            _selectedTask = null;
            _taskService.Delete(idToDelete);
            RefreshList();
        }
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditEvents || _selectedTask == null) return;
        string newTitle = TitleBox.Text;
        _taskService.Update(_selectedTask.Id, t => t.Title = newTitle);
    }

    private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditEvents || _selectedTask == null) return;
        string newDesc = DescriptionBox.Text;
        _taskService.Update(_selectedTask.Id, t => t.Description = newDesc);
    }

    private void PriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEditEvents || _selectedTask == null) return;
        var priority = PriorityCombo.SelectedIndex switch
        {
            0 => TaskPriority.Low,
            2 => TaskPriority.High,
            3 => TaskPriority.Urgent,
            _ => TaskPriority.Normal
        };
        _taskService.Update(_selectedTask.Id, t => t.Priority = priority);
    }

    private void DueDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEditEvents || _selectedTask == null) return;
        var newDate = DueDatePicker.SelectedDate;
        _taskService.Update(_selectedTask.Id, t => t.DueDate = newDate);
    }

    private void ClearDueDateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) return;
        DueDatePicker.SelectedDate = null;
    }

    private void EstMinutesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEditEvents || _selectedTask == null) return;
        int mins = EstMinutesCombo.SelectedIndex switch
        {
            1 => 25,
            2 => 50,
            3 => 75,
            4 => 100,
            _ => 0
        };
        _taskService.Update(_selectedTask.Id, t => t.EstimatedMinutes = mins);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        HandleKeyDown(e.Key, Keyboard.Modifiers, e);
    }

    internal void HandleKeyDown(Key key, ModifierKeys modifiers, System.Windows.Input.KeyEventArgs? e = null)
    {
        if (key == Key.P && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            App.CurrentApp?.ToggleCommandPalette();
            if (e != null) e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.D1 or Key.NumPad1:
                    SelectTab("Today");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D2 or Key.NumPad2:
                    SelectTab("Upcoming");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D3 or Key.NumPad3:
                    SelectTab("All");
                    if (e != null) e.Handled = true;
                    return;
                case Key.D4 or Key.NumPad4:
                    SelectTab("Completed");
                    if (e != null) e.Handled = true;
                    return;
                case Key.N:
                    NewTaskBtn_Click(this, new RoutedEventArgs());
                    if (e != null) e.Handled = true;
                    return;
                case Key.F:
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                    if (e != null) e.Handled = true;
                    return;
                case Key.Enter:
                    if (_selectedTask != null)
                    {
                        StartFocusBtn_Click(this, new RoutedEventArgs());
                        if (e != null) e.Handled = true;
                        return;
                    }
                    break;
                case Key.W:
                    Close();
                    if (e != null) e.Handled = true;
                    return;
                case Key.R:
                    RefreshList();
                    if (e != null) e.Handled = true;
                    return;
            }
        }

        // F5 refresh task list
        if (key == Key.F5 && modifiers == ModifierKeys.None)
        {
            RefreshList();
            if (e != null) e.Handled = true;
            return;
        }

        // Ctrl+Tab / Ctrl+Shift+Tab (Tab cycling)
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control && key == Key.Tab)
        {
            string[] tabs = ["Today", "Upcoming", "All", "Completed"];
            int currentIndex = Array.IndexOf(tabs, _activeTab);
            if (currentIndex < 0) currentIndex = 0;

            bool reverse = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            int nextIndex = reverse
                ? (currentIndex - 1 + tabs.Length) % tabs.Length
                : (currentIndex + 1) % tabs.Length;

            SelectTab(tabs[nextIndex]);
            if (e != null) e.Handled = true;
            return;
        }
        else if (key == Key.Delete && modifiers == ModifierKeys.None)
        {
            // Only trigger delete if focused on the task list
            if (TasksListBox.IsKeyboardFocusWithin && !TitleBox.IsKeyboardFocusWithin && !DescriptionBox.IsKeyboardFocusWithin && !SearchBox.IsKeyboardFocusWithin)
            {
                if (_selectedTask != null)
                {
                    DeleteTaskBtn_Click(this, new RoutedEventArgs());
                    if (e != null) e.Handled = true;
                }
            }
        }
        else if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            // Only toggle complete if focused on the task list
            if (TasksListBox.IsKeyboardFocusWithin && _selectedTask != null)
            {
                _taskService.ToggleComplete(_selectedTask.Id);
                RefreshList();
                if (e != null) e.Handled = true;
            }
        }
        else if (key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = "";
                if (e != null) e.Handled = true;
                return;
            }

            if (TitleBox.IsKeyboardFocusWithin || DescriptionBox.IsKeyboardFocusWithin || (FocusManager.GetFocusedElement(this) is TextBox))
            {
                Keyboard.ClearFocus();
                if (e != null) e.Handled = true;
                return;
            }

            Close();
            if (e != null) e.Handled = true;
        }
    }
}
