using DesktopPet.Services;

namespace DesktopPet.Engine;

/// <summary>
/// Local state model for Neku's personality. Tracks mood, energy, familiarity,
/// and recent interactions to make the character feel less repetitive over time.
/// This is a deterministic rules system — no AI model, no network, no inference.
/// </summary>
public sealed class Personality
{
    // ── Active companion tracking ──────────────────────────────────────────
    private string _activePet = "neku";
    public string ActivePet => _activePet;
    public bool IsBillu => string.Equals(_activePet, "billu", StringComparison.OrdinalIgnoreCase);

    public void SetActivePet(string? pet)
    {
        _activePet = string.IsNullOrWhiteSpace(pet) ? "neku" : pet.Trim().ToLowerInvariant();
    }

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

    // ── Billu ("Reel Goblin") dialogue lines ────────────────────────────────
    private static readonly string[] BilluHappyLines =
    [
        "Vibe check passed. You get to live another day ✨",
        "I contribute nothing and receive everything. Flawless system.",
        "Certified loaf. 10/10 no notes.",
        "The algorithm finally blessed my feed today ✨",
        "Look at me living my best drama-free nine lives.",
        "Bro, dopamine hit just landed.",
        "Unbothered. Moisturized. In my lane. Loafing.",
    ];

    private static readonly string[] BilluNeutralLines =
    [
        "Opened one reel. There goes my entire afternoon.",
        "Bro, my last brain cell is buffering.",
        "I contribute nothing and judge everyone.",
        "Mentally offline. Physically loafing.",
        "The way I'm unemployed in every possible sense.",
        "I'm not lazy. I'm in power-saving mode.",
        "Respectfully, what is bro doing?",
        "Bro thinks he's the main character.",
        "The algorithm knows too much.",
        "Just sitting here looking like a whole meme template.",
        "My brain has 48 tabs open and 47 of them are spinning cat videos.",
    ];

    private static readonly string[] BilluAnnoyedLines =
    [
        "Skill issue. Have you tried taking a nap?",
        "Side-eyeing so hard my pupils need GPS.",
        "Unsubscribe from whatever you just did.",
        "Do not perceive me right now.",
        "L + ratio + touch cat grass.",
        "Excuse me, who authorized this disturbance?",
        "My disappointment is unmeasurable and my afternoon is ruined.",
    ];

    private static readonly string[] BilluSleepyLines =
    [
        "Battery at 1%. Initiating emergency desk flop.",
        "Sleep schedule? Never heard of her.",
        "Brain cell fully drained. Do not disturb.",
        "My bedtime was yesterday. Goodnight.",
        "Physically present, mentally on airplane mode.",
    ];

    private static readonly string[] BilluMischievousLines =
    [
        "About to do something completely chaotic and blame orange cat genetics.",
        "Chaos isn't a glitch, it's a feature 😈",
        "They told me to behave. I left that on delivered.",
        "Zoomies loading: 99% complete...",
        "Woke up and chose maximum desktop mischief.",
        "I know I shouldn't knock that off your desk. Counterpoint: gravity test.",
    ];

    private static readonly string[] BilluFocusStartedLines =
    [
        "Focus session? Let's see how long before you check your feed 👀",
        "25 minutes of zero doomscrolling. I believe in you (mostly).",
        "Lock in bro. I'll supervise from this loaf position 🎯",
        "Do not open another tab. I see everything.",
    ];

    private static readonly string[] BilluFocusCompletedLines =
    [
        "Session completed! You actually didn't doomscroll! 🏆",
        "Look at bro being productive. Certified main character moment ✨",
        "Focus goal unlocked! Now you've earned a quick meme break.",
        "High five. You cooked that session!",
    ];

    private static readonly string[] BilluBreakCompletedLines =
    [
        "Break's over! Put the phone down, respectfully.",
        "Back to work! That reel wasn't even that good anyway.",
        "Recharge complete. Back to the grind, chief.",
        "Time to lock back in. Don't make me side-eye you.",
    ];

    private static readonly string[] BilluBuildSuccessLines =
    [
        "Zero errors?! Bro actually cooked! 👨‍🍳🔥",
        "Green build! The algorithm smiles upon this code ✨",
        "Clean compile! Look at you writing working software!",
        "Build succeeded! We are so unbelievably back.",
    ];

    private static readonly string[] BilluTestFailureLines =
    [
        "Test red? Skill issue detected 🐛",
        "Bro... what did that assert ever do to you?",
        "Emotional damage in line 42 🙀",
        "Tests failed. Have you tried taking a nap and seeing if it fixes itself?",
    ];

    private static readonly string[] BilluBoopLines =
    [
        "Bro booped the screen. Revolutionary.",
        "Did you really just poke my nose?",
        "Boop registered. Deducting 10 aura points.",
        "Boop back! Don't tempt the orange cat.",
        "*suspicious side-eye intensifies*",
    ];

    private static readonly string[] BilluPetLines =
    [
        "Acceptable. My dopamine levels have risen 3%.",
        "Keep the scratches coming and nobody gets roasted.",
        "Okay fine, you're my favorite human for the next 10 seconds 💕",
        "*smug purr intensifies*",
    ];

    private static readonly string[] BilluDragLines =
    [
        "Help, I'm being relocated against my will!",
        "Bro is literally dragging me across the desktop.",
        "Air freight? For free? Luxury travel.",
        "Put me down gently or I'm deleting your browser cache.",
    ];

    private static readonly string[] BilluSurprisedLines =
    [
        "WHAT WAS THAT?! Bro startled my soul.",
        "JUMP SCARE WARNING NEXT TIME PLEASE.",
        "My orange ears picked up an unregistered frequency.",
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
        if (!string.IsNullOrWhiteSpace(settings.ActivePet))
            _activePet = settings.ActivePet.Trim().ToLowerInvariant();
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

            case "chaotic":
                _mood = 0.85;
                _energy = 0.95;
                _mischief = 0.95;
                _familiarity = 0.75;
                if (settings != null)
                {
                    settings.PersonalityPreset = "Chaotic";
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
        "chaotic" => "Unhinged energy, maximum mischief, rapid reactions, and meme-worthy quips.",
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
        if (IsBillu)
        {
            if (_energy < 0.2)
                pool = BilluSleepyLines;
            else if (_mischief > 0.7)
                pool = BilluMischievousLines;
            else if (_mood > 0.7)
                pool = BilluHappyLines;
            else if (_mood < 0.3)
                pool = BilluAnnoyedLines;
            else
                pool = BilluNeutralLines;
        }
        else
        {
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
        }

        return PickNonRepeating(pool);
    }

    /// <summary>Get a surprised reaction line.</summary>
    public string GetSurprisedLine() => PickNonRepeating(IsBillu ? BilluSurprisedLines : SurprisedLines);

    /// <summary>Reaction line when a focus session starts.</summary>
    public string GetFocusStartedLine() => PickNonRepeating(IsBillu ? BilluFocusStartedLines : FocusStartedLines);

    /// <summary>Reaction line when a focus session completes successfully.</summary>
    public string GetFocusCompletedLine() => PickNonRepeating(IsBillu ? BilluFocusCompletedLines : FocusCompletedLines);

    /// <summary>Reaction line when a break finishes.</summary>
    public string GetBreakCompletedLine() => PickNonRepeating(IsBillu ? BilluBreakCompletedLines : BreakCompletedLines);

    /// <summary>Reaction line when a developer build passes.</summary>
    public string GetBuildSuccessLine() => PickNonRepeating(IsBillu ? BilluBuildSuccessLines : BuildSuccessLines);

    /// <summary>Reaction line when a developer test fails.</summary>
    public string GetTestFailureLine() => PickNonRepeating(IsBillu ? BilluTestFailureLines : TestFailureLines);

    /// <summary>Reaction line when the pet is booped (tapped).</summary>
    public string GetBoopLine() => PickNonRepeating(IsBillu ? BilluBoopLines : BoopLines);

    /// <summary>Reaction line when the pet is petted.</summary>
    public string GetPetLine() => PickNonRepeating(IsBillu ? BilluPetLines : PetLines);

    /// <summary>Reaction line when the pet is dragged.</summary>
    public string GetDragLine() => PickNonRepeating(IsBillu ? BilluDragLines : DragLines);


    /// <summary>Determine the dominant emotional state for animation selection.</summary>
    public PetState SuggestEmotionalState()
    {
        if (_energy < 0.15) return PetState.Sleep;
        if (_mood > 0.8 && _mischief > 0.6) return PetState.Zoomies;
        if (IsBillu && _mischief > 0.7 && _energy > 0.5) return PetState.Zoomies;
        if (_mood > 0.7) return PetState.Pet;  // happy face
        if (_mood < 0.25) return PetState.Idle; // grumpy just sits
        return PetState.Idle;
    }

    /// <summary>Whether Neku should display a random reaction right now.</summary>
    public bool ShouldReact(double chanceMultiplier = 1.0)
    {
        double chance = 0.005 * chanceMultiplier;
        if (_familiarity > 0.5) chance *= 1.5;  // more expressive with familiar users
        if (_mischief > 0.6) chance *= 2.0;     // mischievous companion reacts more
        if (IsBillu) chance *= 1.4;             // Billu has extra reactive meme energy
        return _rng.NextDouble() < chance;
    }
}
