using System.Windows;
using DesktopPet.Engine;
using DesktopPet.Services;

namespace DesktopPet.UI;

public partial class DeveloperEventsWindow : Window
{
    private readonly DeveloperEventService? _service;
    private readonly PetEngine _engine;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;

    public DeveloperEventsWindow(DeveloperEventService? service, PetEngine engine, AppSettings settings, SettingsService settingsService)
    {
        InitializeComponent();
        _service = service;
        _engine = engine;
        _settings = settings;
        _settingsService = settingsService;

        UpdateUiState();
    }

    private void UpdateUiState()
    {
        int port = _settings.DeveloperPort;
        EndpointText.Text = $"http://127.0.0.1:{port}/";
        CommandSnippetBox.Text = $"curl -s http://127.0.0.1:{port}/?type=build_success";

        if (_settings.EnableDeveloperEvents)
        {
            StatusText.Text = $"Status: Listening on port {port}";
            StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            ToggleListenerBtn.Content = "Disable";
        }
        else
        {
            StatusText.Text = "Status: Disabled (zero network/ports)";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
            ToggleListenerBtn.Content = "Enable";
        }
    }

    private void ToggleListenerBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.EnableDeveloperEvents = !_settings.EnableDeveloperEvents;
        if (_settings.EnableDeveloperEvents)
        {
            _service?.Start();
        }
        else
        {
            _service?.Stop();
        }
        _settingsService.Save();
        UpdateUiState();
    }

    private void BuildSuccess_Click(object sender, RoutedEventArgs e)
    {
        _service?.HandleEvent("build_success");
    }

    private void BuildFailure_Click(object sender, RoutedEventArgs e)
    {
        _service?.HandleEvent("build_failure");
    }

    private void TestsPass_Click(object sender, RoutedEventArgs e)
    {
        _service?.HandleEvent("test_pass");
    }

    private void TestsFail_Click(object sender, RoutedEventArgs e)
    {
        _service?.HandleEvent("test_fail");
    }
}
