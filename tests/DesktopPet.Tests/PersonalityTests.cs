using DesktopPet.Engine;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class PersonalityTests
{
    [Fact]
    public void Initial_drives_are_within_valid_range()
    {
        var p = new Personality();
        Assert.InRange(p.Mood, 0.0, 1.0);
        Assert.InRange(p.Energy, 0.0, 1.0);
        Assert.InRange(p.Familiarity, 0.0, 1.0);
        Assert.InRange(p.Mischief, 0.0, 1.0);
        Assert.Equal(0, p.TotalPets);
        Assert.Equal(0, p.TotalBoops);
    }

    [Fact]
    public void Tick_keeps_drives_within_valid_range_under_prolonged_activity()
    {
        var p = new Personality();
        for (int i = 0; i < 5000; i++)
        {
            p.Tick(0.1, PetState.Zoomies);
            Assert.InRange(p.Mood, 0.0, 1.0);
            Assert.InRange(p.Energy, 0.0, 1.0);
            Assert.InRange(p.Mischief, 0.0, 1.0);
        }
    }

    [Fact]
    public void Tick_recovers_energy_during_sleep()
    {
        var p = new Personality();
        // Drain energy first
        for (int i = 0; i < 1000; i++) p.Tick(0.1, PetState.Zoomies);
        double lowEnergy = p.Energy;

        // Sleep to recover
        for (int i = 0; i < 500; i++) p.Tick(0.1, PetState.Sleep);
        Assert.True(p.Energy > lowEnergy);
    }

    [Fact]
    public void Petting_increases_mood_and_familiarity()
    {
        var p = new Personality();
        double initialMood = p.Mood;
        double initialFamiliarity = p.Familiarity;

        p.OnPetted();

        Assert.Equal(1, p.TotalPets);
        Assert.True(p.Mood >= initialMood);
        Assert.True(p.Familiarity > initialFamiliarity);
    }

    [Fact]
    public void Booping_increases_boops_and_mischief()
    {
        var p = new Personality();
        double initialMischief = p.Mischief;

        p.OnBooped();

        Assert.Equal(1, p.TotalBoops);
        Assert.True(p.Mischief > initialMischief);
    }

    [Fact]
    public void Dragging_decreases_mood()
    {
        var p = new Personality();
        double initialMood = p.Mood;

        p.OnDragged();

        Assert.True(p.Mood < initialMood);
    }

    [Fact]
    public void Positive_and_negative_events_influence_mood()
    {
        var p = new Personality();
        p.OnPositiveEvent();
        double boosted = p.Mood;

        p.OnNegativeEvent();
        Assert.True(p.Mood < boosted);
    }

    [Fact]
    public void Dialogue_lines_are_non_empty_and_valid()
    {
        var p = new Personality();
        for (int i = 0; i < 50; i++)
        {
            var line = p.GetDialogueLine();
            Assert.NotNull(line);
            Assert.NotEmpty(line);

            var surprise = p.GetSurprisedLine();
            Assert.NotNull(surprise);
            Assert.NotEmpty(surprise);
        }
    }

    [Fact]
    public void SaveInto_and_LoadFrom_persists_and_clamps_state()
    {
        var settings = new AppSettings();
        var p1 = new Personality();
        p1.OnPetted();
        p1.OnBooped();
        p1.SaveInto(settings);

        var p2 = new Personality();
        p2.LoadFrom(settings);

        Assert.Equal(p1.TotalPets, p2.TotalPets);
        Assert.Equal(p1.TotalBoops, p2.TotalBoops);
        Assert.Equal(p1.Mood, p2.Mood, 4);
        Assert.Equal(p1.Energy, p2.Energy, 4);

        // Clamping check
        settings.PersonalityMood = 99.0;
        settings.PersonalityEnergy = -5.0;
        var p3 = new Personality();
        p3.LoadFrom(settings);
        Assert.Equal(1.0, p3.Mood);
        Assert.Equal(0.0, p3.Energy);
    }

    [Fact]
    public void Personality_tracks_active_companion_and_switches_dialogue()
    {
        var p = new Personality();
        Assert.Equal("neku", p.ActivePet);
        Assert.False(p.IsBillu);

        p.SetActivePet("billu");
        Assert.Equal("billu", p.ActivePet);
        Assert.True(p.IsBillu);

        // Gather lines from dialogue getters to confirm Billu-specific quips are returned
        var billuLines = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            var line = p.GetDialogueLine();
            if (line != null) billuLines.Add(line);
        }

        // Verify key Billu quips appear in pool
        Assert.Contains(billuLines, l =>
            l.Contains("buffering", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("afternoon", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("unemployed", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("loafing", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("power-saving", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("main character", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("algorithm", StringComparison.OrdinalIgnoreCase) ||
            l.Contains("vibe check", StringComparison.OrdinalIgnoreCase));

        // Reaction lines
        string boops = string.Concat(Enumerable.Range(0, 10).Select(_ => p.GetBoopLine()));
        Assert.Contains("aura points", boops, StringComparison.OrdinalIgnoreCase);

        string focus = string.Concat(Enumerable.Range(0, 10).Select(_ => p.GetFocusCompletedLine()));
        Assert.Contains("doomscroll", focus, StringComparison.OrdinalIgnoreCase);

        string tests = string.Concat(Enumerable.Range(0, 10).Select(_ => p.GetTestFailureLine()));
        Assert.Contains("skill issue", tests, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Personality_supports_chaotic_preset()
    {
        var settings = new AppSettings();
        var p = new Personality();
        p.ApplyPresetDrives("Chaotic", settings);

        Assert.Equal("Chaotic", settings.PersonalityPreset);
        Assert.True(settings.EnableRandomReactions);
        Assert.True(settings.EnableScrollPlay);
        Assert.True(settings.EnableFocusReactions);

        Assert.InRange(p.Mood, 0.8, 1.0);
        Assert.InRange(p.Energy, 0.9, 1.0);
        Assert.InRange(p.Mischief, 0.9, 1.0);

        string desc = Personality.GetPresetDescription("Chaotic");
        Assert.Contains("Unhinged", desc, StringComparison.OrdinalIgnoreCase);

        // AppSettings validation keeps Chaotic
        settings.Validate();
        Assert.Equal("Chaotic", settings.PersonalityPreset);
    }

    [Fact]
    public void Switching_from_billu_restores_normal_personality_dialogue()
    {
        var p = new Personality();
        p.SetActivePet("neku");
        Assert.False(p.IsBillu);

        // Under neku, boop does not mention aura points
        for (int i = 0; i < 20; i++)
        {
            Assert.DoesNotContain("aura points", p.GetBoopLine(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("doomscroll", p.GetFocusCompletedLine(), StringComparison.OrdinalIgnoreCase);
        }

        // Switch to billu
        p.SetActivePet("billu");
        Assert.True(p.IsBillu);
        string billuBoops = string.Concat(Enumerable.Range(0, 15).Select(_ => p.GetBoopLine()));
        Assert.Contains("aura points", billuBoops, StringComparison.OrdinalIgnoreCase);

        // Switch back to classic cat
        p.SetActivePet("cat");
        Assert.False(p.IsBillu);
        Assert.Equal("cat", p.ActivePet);
        for (int i = 0; i < 20; i++)
        {
            Assert.DoesNotContain("aura points", p.GetBoopLine(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("doomscroll", p.GetFocusCompletedLine(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Dialogue_does_not_repeat_immediately()
    {
        var p = new Personality();
        p.SetActivePet("billu");

        string? prev = null;
        int nonDuplicates = 0;
        for (int i = 0; i < 30; i++)
        {
            var current = p.GetDialogueLine();
            if (prev != null && current != null && current != prev)
            {
                nonDuplicates++;
            }
            prev = current;
        }

        // Non-repeating queue should ensure majority of transitions are distinct
        Assert.True(nonDuplicates >= 25, $"Expected at least 25 non-duplicate transitions out of 30, got {nonDuplicates}");
    }
}
