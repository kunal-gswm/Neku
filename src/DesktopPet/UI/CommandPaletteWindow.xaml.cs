using System.Windows;
using System.Windows.Input;
using DesktopPet.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DesktopPet.UI;

/// <summary>
/// Centered keyboard-driven overlay command palette for fast navigation and action execution.
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly CommandPaletteService _service;
    private bool _isExecuting;

    public CommandPaletteWindow(CommandPaletteService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        InitializeComponent();
        RefreshResults();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchInputBox.Focus();
        Keyboard.Focus(SearchInputBox);
        RefreshResults();
    }

    private void SearchInputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(SearchInputBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        RefreshResults();
    }

    private void RefreshResults()
    {
        var results = _service.Search(SearchInputBox.Text);
        ResultsListBox.ItemsSource = results;

        bool hasResults = results.Count > 0;
        EmptyStatePanel.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
        ResultsListBox.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;

        CommandCountText.Text = $"{results.Count} {(results.Count == 1 ? "command" : "commands")}";

        if (hasResults)
        {
            ResultsListBox.SelectedIndex = 0;
        }
    }

    private void SearchInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (ResultsListBox.Items.Count > 0 && ResultsListBox.SelectedIndex < ResultsListBox.Items.Count - 1)
                {
                    ResultsListBox.SelectedIndex++;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (ResultsListBox.Items.Count > 0 && ResultsListBox.SelectedIndex > 0)
                {
                    ResultsListBox.SelectedIndex--;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Enter:
                ExecuteSelected();
                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ResultsListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelected();
    }

    private void ExecuteSelected()
    {
        if (_isExecuting) return;
        if (ResultsListBox.SelectedItem is not PaletteCommand cmd) return;

        _isExecuting = true;
        Close();

        // Dispatch execution immediately following window dismissal
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                _service.Execute(cmd);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[CommandPalette] Error executing command '{cmd.Id}': {ex.Message}");
            }
        });
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_isExecuting)
        {
            Close();
        }
    }
}
