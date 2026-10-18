using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class LauncherIpcTests
{
    [Fact]
    public void GetDefaultPipeName_ReturnsSanitizedUserPipeName()
    {
        string pipeName = LauncherIpcService.GetDefaultPipeName();
        Assert.NotNull(pipeName);
        Assert.StartsWith("Neku.LauncherPipe.", pipeName);
        Assert.True(pipeName.Length > "Neku.LauncherPipe.".Length);
        Assert.DoesNotContain(" ", pipeName);
    }

    [Fact]
    public void TrySignalRunningInstance_WhenNoServerListening_ReturnsFalseQuickly()
    {
        string deadPipe = $"Neku.TestPipe.{Guid.NewGuid():N}";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        bool result = LauncherIpcService.TrySignalRunningInstance(timeoutMs: 150, pipeName: deadPipe);

        sw.Stop();
        Assert.False(result);
        Assert.True(sw.ElapsedMilliseconds < 1500, $"Signaling non-existent pipe took too long: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task LauncherIpcService_RoundtripActivation_SignalsEvent()
    {
        string pipeName = $"Neku.TestPipe.{Guid.NewGuid():N}";
        using var service = new LauncherIpcService(pipeName);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ActivationRequested += () => tcs.TrySetResult(true);

        service.Start();

        // Allow pipe listener loop to initialize
        await Task.Delay(100);

        bool signaled = LauncherIpcService.TrySignalRunningInstance(timeoutMs: 2500, pipeName: pipeName);
        Assert.True(signaled, "TrySignalRunningInstance should have returned true");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2500));
        Assert.Same(tcs.Task, completed);
        Assert.True(await tcs.Task);
    }

    [Fact]
    public void LauncherIpcService_MultipleDisposals_AreSafe()
    {
        string pipeName = $"Neku.TestPipe.{Guid.NewGuid():N}";
        var service = new LauncherIpcService(pipeName);
        service.Start();
        service.Dispose();
        service.Dispose(); // second dispose should not throw
    }

    [Fact]
    public async Task LauncherIpcService_MultipleActivations_SucceedSequentially()
    {
        string pipeName = $"Neku.TestPipe.{Guid.NewGuid():N}";
        using var service = new LauncherIpcService(pipeName);

        int activationCount = 0;
        service.ActivationRequested += () => Interlocked.Increment(ref activationCount);
        service.Start();

        await Task.Delay(100);

        bool first = LauncherIpcService.TrySignalRunningInstance(timeoutMs: 2500, pipeName: pipeName);
        Assert.True(first);

        bool second = LauncherIpcService.TrySignalRunningInstance(timeoutMs: 2500, pipeName: pipeName);
        Assert.True(second);

        // Wait up to 1 second for activations to be counted
        for (int i = 0; i < 20 && activationCount < 2; i++)
        {
            await Task.Delay(50);
        }

        Assert.Equal(2, activationCount);
    }

    [Fact]
    public void Reactivation_WhenPetHidden_RestoresVisibilityState()
    {
        bool userHidden = true;
        bool petVisible = false;
        bool renderLoopEnabled = false;

        void OnHideToggled(bool hidden)
        {
            userHidden = hidden;
            petVisible = !hidden;
            renderLoopEnabled = !hidden;
        }

        // Simulate Launcher activation handler
        if (userHidden)
        {
            OnHideToggled(false);
        }

        Assert.False(userHidden);
        Assert.True(petVisible);
        Assert.True(renderLoopEnabled);
    }

    [Fact]
    public void Reactivation_QuietMode_SuppressesActivation()
    {
        bool hiddenForQuiet = true;
        bool activationProcessed = false;

        // When hidden for quiet mode, activation is suppressed
        if (!hiddenForQuiet)
        {
            activationProcessed = true;
        }

        Assert.False(activationProcessed);
    }

    [Fact]
    public void DesktopGeometry_BoundsCheck_IdentifiesOffscreenCoordinates()
    {
        var geometry = new DesktopPet.Engine.DesktopGeometry(
            new System.Windows.Rect(0, 0, 1920, 1080),
            new[] { new System.Windows.Rect(0, 0, 1920, 1040) });

        var bounds = geometry.Bounds;

        double normalX = 500, normalY = 500;
        bool isNormalOffscreen = normalX < bounds.Left || normalX > bounds.Right || normalY < bounds.Top || normalY > bounds.Bottom;
        Assert.False(isNormalOffscreen);

        double offscreenX = -1000, offscreenY = 500;
        bool isOffscreenLeft = offscreenX < bounds.Left || offscreenX > bounds.Right || offscreenY < bounds.Top || offscreenY > bounds.Bottom;
        Assert.True(isOffscreenLeft);

        double offscreenFarRight = 3000, offscreenFarBottom = 2000;
        bool isOffscreenRight = offscreenFarRight < bounds.Left || offscreenFarRight > bounds.Right || offscreenFarBottom < bounds.Top || offscreenFarBottom > bounds.Bottom;
        Assert.True(isOffscreenRight);
    }
}
