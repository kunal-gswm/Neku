using System.Windows.Media;
using DesktopPet.Engine;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class CompanionStudioTests
{
    private sealed class DummyPetView : IPetView
    {
        public double DpiScale => 1.0;
        public void Render(ImageSource frame, double left, double top, int facing) { }
        public void SetPetSize(double w, double h) { }
        public void SetHeatLevel(double level) { }
        public void SpawnHearts() { }
        public void DrawToiletPaper(double catLeft, double catTop, double catW, double catH, double length) { }
        public void ClearToiletPaper() { }
        public void DropPebble(double screenX, double screenY) { }
        public void DrawSpeechBubble(double catLeft, double catTop, double catW, double catH, string text) { }
        public void ClearSpeechBubble() { }
        public void SetPetVisible(bool visible) { }
    }

    [Fact]
    public void DiscoverPacks_returns_enriched_pack_metadata()
    {
        var packs = AssetSource.DiscoverPacks();
        Assert.NotEmpty(packs);

        var nekuPack = packs.FirstOrDefault(p => p.Id.Equals("neku", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(nekuPack);
        Assert.Equal("Neku (Goblin companion)", nekuPack.DisplayName);
        Assert.True(nekuPack.IsValid);
        Assert.False(nekuPack.IsCustom);
        Assert.NotNull(nekuPack.Description);
        Assert.True(nekuPack.AnimationCount >= 10);
        Assert.NotNull(nekuPack.Animations);
        Assert.Contains("idle", nekuPack.Animations);
        Assert.Contains("wave", nekuPack.Animations);
        Assert.Contains("celebrate", nekuPack.Animations);
        Assert.Equal(128, nekuPack.CellWidth);
        Assert.Equal(128, nekuPack.CellHeight);
    }

    [Fact]
    public void CreateAnimator_returns_isolated_animator_for_valid_pack()
    {
        var animator = AssetSource.CreateAnimator("neku");
        Assert.NotNull(animator);
        Assert.Equal("neku", animator.Manifest.Name);
        Assert.True(animator.Manifest.Animations.ContainsKey("idle"));
        Assert.NotNull(animator.GetPreviewFrame("idle"));

        // Switching animation on isolated animator
        animator.Play("wave");
        Assert.Equal("wave", animator.CurrentAnimationName);
    }

    [Fact]
    public void CreateAnimator_returns_null_for_nonexistent_pack()
    {
        var animator = AssetSource.CreateAnimator("pack_that_does_not_exist_xyz_999");
        Assert.Null(animator);
    }

    [Fact]
    public void Personality_ApplyPreset_Calm_configures_settings()
    {
        var settings = new AppSettings();
        Personality.ApplyPreset("Calm", settings);

        Assert.Equal("Calm", settings.PersonalityPreset);
        Assert.False(settings.EnableRandomReactions);
        Assert.False(settings.EnableScrollPlay);
        Assert.InRange(settings.PersonalityMood, 0.65, 0.75);
        Assert.InRange(settings.PersonalityEnergy, 0.25, 0.35);
    }

    [Fact]
    public void Personality_ApplyPreset_Playful_configures_settings()
    {
        var settings = new AppSettings();
        Personality.ApplyPreset("Playful", settings);

        Assert.Equal("Playful", settings.PersonalityPreset);
        Assert.True(settings.EnableRandomReactions);
        Assert.True(settings.EnableScrollPlay);
        Assert.InRange(settings.PersonalityMood, 0.75, 0.85);
        Assert.InRange(settings.PersonalityEnergy, 0.7, 0.8);
    }

    [Fact]
    public void Personality_ApplyPreset_Expressive_enables_all_reactions()
    {
        var settings = new AppSettings();
        Personality.ApplyPreset("Expressive", settings);

        Assert.Equal("Expressive", settings.PersonalityPreset);
        Assert.True(settings.EnableRandomReactions);
        Assert.True(settings.EnableScrollPlay);
        Assert.True(settings.EnableFocusReactions);
        Assert.True(settings.EnableBreakReactions);
        Assert.True(settings.EnablePettingReactions);
    }

    [Fact]
    public void Personality_RestoreDefaults_resets_scale_speed_opacity_and_reactions()
    {
        var settings = new AppSettings
        {
            SizeScale = 1.8,
            Speed = 2.2,
            Opacity = 0.5,
            PersonalityPreset = "Calm",
            EnableRandomReactions = false,
            EnableFocusReactions = false,
            ReducedMotion = true,
            EnableQuietMode = true
        };

        Personality.RestoreDefaults(settings);

        Assert.Equal(1.0, settings.SizeScale);
        Assert.Equal(1.0, settings.Speed);
        Assert.Equal(1.0, settings.Opacity);
        Assert.Equal("Expressive", settings.PersonalityPreset);
        Assert.True(settings.EnableAutonomousWandering);
        Assert.True(settings.EnableRandomReactions);
        Assert.True(settings.EnableFocusReactions);
        Assert.True(settings.EnableBreakReactions);
        Assert.True(settings.EnablePettingReactions);
        Assert.True(settings.ReducedMotion);
        Assert.True(settings.EnableQuietMode);
    }

    [Fact]
    public void AppSettings_Validate_clamps_out_of_range_values()
    {
        var settings = new AppSettings
        {
            SizeScale = 99.0,
            Speed = -5.0,
            Opacity = 0.05,
            PersonalityPreset = "UnknownNonsensePreset"
        };

        settings.Validate();

        Assert.Equal(2.0, settings.SizeScale);
        Assert.Equal(0.3, settings.Speed);
        Assert.Equal(0.2, settings.Opacity);
        Assert.Equal("Expressive", settings.PersonalityPreset);
    }

    [Fact]
    public void AppSettings_Validate_clamps_underflow_values()
    {
        var settings = new AppSettings
        {
            SizeScale = 0.1,
            Speed = 10.0,
            Opacity = 5.0,
            PersonalityPreset = "Playful"
        };

        settings.Validate();

        Assert.Equal(0.5, settings.SizeScale);
        Assert.Equal(2.5, settings.Speed);
        Assert.Equal(1.0, settings.Opacity);
        Assert.Equal("Playful", settings.PersonalityPreset);
    }

    [Fact]
    public void GetPresetDescription_returns_valid_descriptions_for_all_presets()
    {
        var calmDesc = Personality.GetPresetDescription("Calm");
        var playfulDesc = Personality.GetPresetDescription("Playful");
        var expressiveDesc = Personality.GetPresetDescription("Expressive");
        var customDesc = Personality.GetPresetDescription("Custom");

        Assert.False(string.IsNullOrWhiteSpace(calmDesc));
        Assert.False(string.IsNullOrWhiteSpace(playfulDesc));
        Assert.False(string.IsNullOrWhiteSpace(expressiveDesc));
        Assert.False(string.IsNullOrWhiteSpace(customDesc));

        Assert.NotEqual(calmDesc, playfulDesc);
        Assert.NotEqual(playfulDesc, expressiveDesc);
    }

    [Fact]
    public void PetEngine_SwitchAnimator_updates_animator_smoothly()
    {
        var settings = new AppSettings();
        var catAnim = AssetSource.CreateAnimator("cat");
        Assert.NotNull(catAnim);

        var mockView = new DummyPetView();
        var surfaceProvider = new SurfaceProvider(() => IntPtr.Zero);
        var sm = new StateMachine(settings);
        var engine = new PetEngine(mockView, catAnim, surfaceProvider, sm, settings);

        Assert.Equal(catAnim, engine.Animator);
        Assert.Equal("idle", engine.Animator.CurrentAnimationName);

        var nekuAnim = AssetSource.CreateAnimator("neku");
        Assert.NotNull(nekuAnim);

        engine.SwitchAnimator(nekuAnim);

        Assert.Equal(nekuAnim, engine.Animator);
        Assert.Equal("idle", engine.Animator.CurrentAnimationName);
    }

    [Fact]
    public void Privacy_pack_display_names_do_not_expose_absolute_paths()
    {
        var packs = AssetSource.DiscoverPacks();
        foreach (var pack in packs)
        {
            Assert.DoesNotContain("C:\\", pack.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("D:\\", pack.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/users/", pack.DisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\\Users\\", pack.DisplayName, StringComparison.OrdinalIgnoreCase);

            if (pack.Description != null)
            {
                Assert.DoesNotContain("C:\\", pack.Description, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("D:\\", pack.Description, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("\\Users\\", pack.Description, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ValidatePack_rejects_empty_and_nonexistent_packs()
    {
        var (emptyValid, emptyError) = AssetSource.ValidatePack("");
        Assert.False(emptyValid);
        Assert.NotNull(emptyError);

        var (missingValid, missingError) = AssetSource.ValidatePack("non_existent_companion_xyz_404");
        Assert.False(missingValid);
        Assert.NotNull(missingError);
    }

    [Fact]
    public void Accessibility_QuietMode_and_ReducedMotion_precedence()
    {
        var settings = new AppSettings
        {
            ReducedMotion = true,
            EnableQuietMode = true
        };

        // Applying even an active preset like "Playful" does not reset user's accessibility preferences
        Personality.ApplyPreset("Playful", settings);

        Assert.True(settings.ReducedMotion);
        Assert.True(settings.EnableQuietMode);
    }
}
