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
}
