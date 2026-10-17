using System.Windows;
using System.Windows.Threading;

namespace DesktopPet.UI;

public partial class FocusNotificationWindow : Window
{
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly Action? _onAction;

    public FocusNotificationWindow(string title, string subtitle, string icon, string actionLabel, Action? onAction = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubText.Text = subtitle;
        IconText.Text = icon;
        ActionButton.Content = actionLabel;
        _onAction = onAction;

        PositionAtBottomRight();

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            _autoCloseTimer.Stop();
            Close();
        };
        _autoCloseTimer.Start();
        Closed += (_, _) => _autoCloseTimer.Stop();
    }

    private void PositionAtBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();
        _onAction?.Invoke();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        Close();
    }
}
