using DesktopPet.Services;

namespace DesktopPet.Engine;

/// <summary>
/// Local state model for Neku's personality. Tracks mood, energy, familiarity,
/// and recent interactions to make the character feel less repetitive over time.
/// This is a deterministic rules system — no AI model, no network, no inference.
/// </summary>
public sealed class Personality
{
    // ── Core drives (0..1) ──────────────────────────────────────────────────
    private double _mood        = 0.6;   // 0 = grumpy, 1 = delighted
    private double _energy      = 0.7;   // 0 = exhausted, 1 = wired
    private double _familiarity = 0.1;   // 0 = stranger, 1 = best friend
    private double _mischief    = 0.3;   // 0 = calm, 1 = chaotic goblin

    // ── Interaction history ────────────────────────────────────────────────
    private int    _totalPets;
    private int    _totalBoops;
    private int    _totalDrags;
    private int    _sessionPets;
    private int    _sessionBoops;
    private int    _reactionsUnlocked;   // unlocked reaction count
    private DateTime _lastInteraction = DateTime.Now;

    // ── Drift rates (per second) ───────────────────────────────────────────
    private const double MoodDrift      = 1.0 / 120.0;  // drifts toward 0.5 over ~2 min
    private const double EnergyDrift    = 1.0 / 90.0;
    private const double MischiefDrift  = 1.0 / 180.0;  // slowly calms down
    private const double FamiliarityGain = 0.001;         // per interaction

    // ── Dialogue lines (original, grouped by mood) ─────────────────────────
    private static readonly string[] HappyLines =
    [
        "nyeh heh heh~",
        "*purrs chaotically*",
        "you're not so bad, human",
        "I'll allow this... for now",
        "*happy goblin noises*",
        "do that again!",
        "you passed the vibe check ✨",
        "*wiggles ears*",
        "best. human. ever.",
    ];

    private static readonly string[] NeutralLines =
    [
        "*stares*",
        "...what?",
        "I'm busy doing nothing",
        "*tail flick*",
        "hmm.",
        "carry on, human",
        "*yawns dramatically*",
        "this is my spot now",
    ];

    private static readonly string[] AnnoyedLines =
    [
        "hey! watch it!",
        "*hisses softly*",
        "excuse me??",
        "I was SLEEPING",
        "personal space, human!",
        "again?! really?!",
        "*angry ear twitch*",
        "that's strike two...",
    ];

    private static readonly string[] SurprisedLines =
    [
        "!!",
        "woah!",
        "*startled jump*",
        "didn't see that coming!",
        "WHAT was THAT",
        "*big eyes*",
    ];

    private static readonly string[] SleepyLines =
    [
        "zzz...",
        "*sleepy mumble*",
        "five more minutes...",
        "too... tired... to... care...",
        "*curls up tighter*",
    ];

    private static readonly string[] MischievousLines =
    [
        "I regret nothing 😈",
        "*knocks something off desk*",
        "oops~ was that yours?",
        "chaos is my middle name",
        "*scheming face*",
        "watch this—",
    ];

    private static readonly string[] FocusStartedLines =
    [
        "Focus session started! Let's lock in! 🎯",
        "Timer's running! Shh, I'll be super quiet 🤫",
        "Time to build something awesome! ✨",
        "Focus mode ON! No distractions allowed 🛡️",
        "25 minutes of greatness starts now! 🚀",
        "I'll keep watch over your screen while you focus 👀",
    ];

    private static readonly string[] FocusCompletedLines =
    [
        "Ding! Focus session complete! Amazing work! 🎉",
        "High five! You crushed that session! 🌟",
        "Session done! Stretch those paws and take a break ☕",
        "Focus goal achieved! Look at you go! 🏆",
        "Great focus! Time to rest your eyes for a bit 🌸",
        "You did it! Take 5 and enjoy the win ✨",
    ];

    private static readonly string[] BreakCompletedLines =
    [
        "Break's over! Feeling refreshed? ✨",
        "Batteries recharged! Ready when you are 🚀",
        "Hope you stretched! Time to get back in the zone 💪",
        "Break time finished! What's next on the list? 📝",
        "Welcome back! Let's crush this next session! 🐾",
    ];

    private static readonly string[] BuildSuccessLines =
    [
        "Build succeeded! Clean compile! 🎉",
        "Zero errors, zero warnings! Ship it! 🚢",
        "Look at you go! Build is green! ✨",
        "Compiled like a charm! High five! 🐾",
        "Code compiled cleanly! Smooth as butter 🧈",
    ];

    private static readonly string[] TestFailureLines =
    [
        "Test red?! Don't worry, we'll fix it! 🔧",
        "Assert failed... was it a missing null check? 🐛",
        "Don't sweat it, the tests caught it early! 💪",
        "A test broke?! Wasn't me, I swear! 🙀",
        "Time to debug! I'll provide moral support 🐱",
    ];

    private static readonly string[] BoopLines =
    [
        "*chirps happily*",
        "Boop received! 💕",
        "Hehe tickles!",
        "Again! Boop me again!",
        "What's up, human? :3",
        "Boop! System online ✨",
        "*blinks curiously*",
    ];

    private static readonly string[] PetLines =
    [
        "*purrs softly*",
        "Aww, warm hands~",
        "Best human ever 💕",
        "*leans into the pet*",
        "Head scratches are the best!",
        "*happy purrs*",
    ];

    private static readonly string[] DragLines =
    [
        "Wheee! Flying!",
        "Where are we relocating to?",
        "Hold on tight!",
        "Air traffic control, clearing for landing ✈️",
        "Put me down gently please! :3",
    ];

    private readonly Random _rng = new();
    private readonly Queue<string> _recentLines = new();
    private const int MaxRecentHistory = 6;

    private string PickNonRepeating(string[] pool)
    {
        if (pool == null || pool.Length == 0) return "...";
        if (pool.Length == 1) return pool[0];

        // Find items not recently spoken
        var candidates = pool.Where(l => !_recentLines.Contains(l)).ToArray();
        string chosen = candidates.Length > 0
            ? candidates[_rng.Next(candidates.Length)]
            : pool[_rng.Next(pool.Length)];

        _recentLines.Enqueue(chosen);
        while (_recentLines.Count > MaxRecentHistory)
            _recentLines.Dequeue();

        return chosen;
    }


    public double Mood        => _mood;
    public double Energy      => _energy;
    public double Familiarity => _familiarity;
    public double Mischief    => _mischief;
    public int    TotalPets   => _totalPets;
    public int    TotalBoops  => _totalBoops;

    /// <summary>Load personality state from settings.</summary>
    public void LoadFrom(AppSettings settings)
    {
        _mood        = Math.Clamp(settings.PersonalityMood, 0, 1);
        _energy      = Math.Clamp(settings.PersonalityEnergy, 0, 1);
        _familiarity = Math.Clamp(settings.PersonalityFamiliarity, 0, 1);
        _mischief    = Math.Clamp(settings.PersonalityMischief, 0, 1);
        _totalPets   = Math.Max(0, settings.TotalPets);
        _totalBoops  = Math.Max(0, settings.TotalBoops);
        _totalDrags  = Math.Max(0, settings.TotalDrags);
        _reactionsUnlocked = Math.Max(0, settings.ReactionsUnlocked);
    }

    /// <summary>Save personality state into settings for persistence.</summary>
    public void SaveInto(AppSettings settings)
    {
        settings.PersonalityMood        = _mood;
        settings.PersonalityEnergy      = _energy;
        settings.PersonalityFamiliarity = _familiarity;
        settings.PersonalityMischief    = _mischief;
        settings.TotalPets              = _totalPets;
        settings.TotalBoops             = _totalBoops;
        settings.TotalDrags             = _totalDrags;
        settings.ReactionsUnlocked      = _reactionsUnlocked;
    }

    /// <summary>
    /// Applies a standard personality preset ("Calm", "Playful", "Expressive", or "Custom")
    /// to the local drives and updates corresponding configuration flags if settings are provided.
    /// </summary>
    public void ApplyPresetDrives(string presetName, AppSettings? settings = null)
    {
        switch (presetName?.ToLowerInvariant())
        {
            case "calm":
                _mood = 0.7;
                _energy = 0.3;
                _mischief = 0.1;
                _familiarity = 0.5;
                if (settings != null)
                {
                    settings.PersonalityPreset = "Calm";
                    settings.EnableRandomReactions = false;
                    settings.EnableScrollPlay = false;
                }
                break;

            case "playful":
                _mood = 0.8;
                _energy = 0.75;
                _mischief = 0.65;
                _familiarity = 0.6;
                if (settings != null)
                {
                    settings.PersonalityPreset = "Playful";
                    settings.EnableRandomReactions = true;
                    settings.EnableScrollPlay = true;
                }
                break;

            case "expressive":
                _mood = 0.9;
                _energy = 0.85;
                _mischief = 0.4;
                _familiarity = 0.85;
                if (settings != null)
                {
                    settings.PersonalityPreset = "Expressive";
                    settings.EnableRandomReactions = true;
                    settings.EnableScrollPlay = true;
                    settings.EnableFocusReactions = true;
                    settings.EnableBreakReactions = true;
                    settings.EnablePettingReactions = true;
                }
                break;

            case "custom":
            default:
                if (settings != null)
                    settings.PersonalityPreset = "Custom";
                break;
        }

        if (settings != null)
            SaveInto(settings);
    }

    /// <summary>Restores default personality drives and sensible interaction settings.</summary>
    public void ResetDrivesToDefault(AppSettings? settings = null)
    {
        _mood = 0.6;
        _energy = 0.7;
        _mischief = 0.3;
        _familiarity = 0.1;

        if (settings != null)
        {
            settings.PersonalityPreset = "Expressive";
            settings.SizeScale = 1.0;
            settings.Speed = 1.0;
            settings.Opacity = 1.0;
            settings.EnableAutonomousWandering = true;
            settings.EnableRandomReactions = true;
            settings.EnableScrollPlay = true;
            settings.EnableFocusReactions = true;
            settings.EnableBreakReactions = true;
            settings.EnablePettingReactions = true;
            // Accessibility preferences (ReducedMotion, EnableQuietMode) remain preserved
            // to avoid overriding user accessibility needs when tuning companion personality.
            SaveInto(settings);
        }
    }

    /// <summary>Static helper to apply a preset to AppSettings.</summary>
    public static void ApplyPreset(string presetName, AppSettings? settings)
    {
        var p = new Personality();
        p.ApplyPresetDrives(presetName, settings);
    }

    /// <summary>Static helper to restore defaults on AppSettings.</summary>
    public static void RestoreDefaults(AppSettings? settings)
    {
        var p = new Personality();
        p.ResetDrivesToDefault(settings);
    }

    /// <summary>Get human-readable description for a preset.</summary>
    public static string GetPresetDescription(string preset) => preset?.ToLowerInvariant() switch
    {
        "calm" => "Gentle and quiet companion with relaxed movement, rare random remarks, and peaceful naps.",
        "playful" => "Energetic and curious companion who loves chasing the cursor, wandering, and lively chatter.",
        "expressive" => "Rich emotional range, celebrating achievements and reacting to focus sessions and code builds.",
        "custom" => "Fine-tune individual reactions, triggers, and appearance settings.",
        _ => "Balanced desktop companion behavior."
    };

    /// <summary>Evolve drives each frame based on elapsed time.</summary>
    public void Tick(double dt, PetState state)
    {
        // Mood drifts toward 0.5 (neutral)
        _mood += (0.5 - _mood) * MoodDrift * dt;

        // Energy affected by activity
        switch (state)
        {
            case PetState.Walk:
            case PetState.Chase:
            case PetState.Zoomies:
            case PetState.Pounce:
            case PetState.Spin:
            case PetState.Jump:
                _energy -= 0.015 * dt;
                break;
            case PetState.Sleep:
            case PetState.Loaf:
            case PetState.SideRest:
                _energy += 0.02 * dt;
                break;
            default:
                _energy += (0.6 - _energy) * EnergyDrift * dt;
                break;
        }

        // Mischief slowly decays toward baseline
        _mischief += (0.3 - _mischief) * MischiefDrift * dt;

        _mood     = Math.Clamp(_mood, 0, 1);
        _energy   = Math.Clamp(_energy, 0, 1);
        _mischief = Math.Clamp(_mischief, 0, 1);
    }

    /// <summary>Record that the user petted Neku.</summary>
    public void OnPetted()
    {
        _totalPets++;
        _sessionPets++;
        _mood = Math.Min(1.0, _mood + 0.05);
        _familiarity = Math.Min(1.0, _familiarity + FamiliarityGain);
        _lastInteraction = DateTime.Now;
    }

    /// <summary>Record a boop (click).</summary>
    public void OnBooped()
    {
        _totalBoops++;
        _sessionBoops++;
        _mood = Math.Min(1.0, _mood + 0.03);
        _mischief = Math.Min(1.0, _mischief + 0.02);
        _familiarity = Math.Min(1.0, _familiarity + FamiliarityGain);
        _lastInteraction = DateTime.Now;
    }

    /// <summary>Record a drag interaction.</summary>
    public void OnDragged()
    {
        _totalDrags++;
        // Too many drags in a row makes Neku annoyed
        if (_totalDrags % 3 == 0)
            _mood = Math.Max(0, _mood - 0.08);
        else
            _mood = Math.Max(0, _mood - 0.02);
        _lastInteraction = DateTime.Now;
    }

    /// <summary>Mood boost from a positive event (build success, etc.)</summary>
    public void OnPositiveEvent()
    {
        _mood = Math.Min(1.0, _mood + 0.15);
        _mischief = Math.Min(1.0, _mischief + 0.05);
    }

    /// <summary>Mood dip from a negative event (test failure, etc.)</summary>
    public void OnNegativeEvent()
    {
        _mood = Math.Max(0, _mood - 0.10);
    }

    /// <summary>Pick a dialogue line appropriate to the current mood.</summary>
    public string? GetDialogueLine()
    {
        string[] pool;
        if (_energy < 0.2)
            pool = SleepyLines;
        else if (_mischief > 0.7)
            pool = MischievousLines;
        else if (_mood > 0.7)
            pool = HappyLines;
        else if (_mood < 0.3)
            pool = AnnoyedLines;
        else
            pool = NeutralLines;

        return PickNonRepeating(pool);
    }

    /// <summary>Get a surprised reaction line.</summary>
    public string GetSurprisedLine() => PickNonRepeating(SurprisedLines);

    /// <summary>Reaction line when a focus session starts.</summary>
    public string GetFocusStartedLine() => PickNonRepeating(FocusStartedLines);

    /// <summary>Reaction line when a focus session completes successfully.</summary>
    public string GetFocusCompletedLine() => PickNonRepeating(FocusCompletedLines);

    /// <summary>Reaction line when a break finishes.</summary>
    public string GetBreakCompletedLine() => PickNonRepeating(BreakCompletedLines);

    /// <summary>Reaction line when a developer build passes.</summary>
    public string GetBuildSuccessLine() => PickNonRepeating(BuildSuccessLines);

    /// <summary>Reaction line when a developer test fails.</summary>
    public string GetTestFailureLine() => PickNonRepeating(TestFailureLines);

    /// <summary>Reaction line when the pet is booped (tapped).</summary>
    public string GetBoopLine() => PickNonRepeating(BoopLines);

    /// <summary>Reaction line when the pet is petted.</summary>
    public string GetPetLine() => PickNonRepeating(PetLines);

    /// <summary>Reaction line when the pet is dragged.</summary>
    public string GetDragLine() => PickNonRepeating(DragLines);


    /// <summary>Determine the dominant emotional state for animation selection.</summary>
    public PetState SuggestEmotionalState()
    {
        if (_energy < 0.15) return PetState.Sleep;
        if (_mood > 0.8 && _mischief > 0.6) return PetState.Zoomies;
        if (_mood > 0.7) return PetState.Pet;  // happy face
        if (_mood < 0.25) return PetState.Idle; // grumpy just sits
        return PetState.Idle;
    }

    /// <summary>Whether Neku should display a random reaction right now.</summary>
    public bool ShouldReact(double chanceMultiplier = 1.0)
    {
        double chance = 0.005 * chanceMultiplier;
        if (_familiarity > 0.5) chance *= 1.5;  // more expressive with familiar users
        if (_mischief > 0.6) chance *= 2.0;     // mischievous Neku reacts more
        return _rng.NextDouble() < chance;
    }
}
