using System.Windows.Media;
using DesktopPet.Engine;
using DesktopPet.Services;
using Xunit;

namespace DesktopPet.Tests;

public class AnimationSystemTests
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

    private static SpriteAnimator CreateAnimator(string? manifestJson = null)
    {
        string json = manifestJson ?? """
        {
          "name": "anim_test",
          "sheet": "neku.png",
          "cellWidth": 128,
          "cellHeight": 128,
          "columns": 4,
          "scale": 1.0,
          "animations": {
            "idle": { "frames": [0, 1], "fps": 4.0, "loop": true },
            "happy": { "frames": [2, 3], "fps": 4.0, "loop": true },
            "wave": { "frames": [4, 5], "fps": 4.0, "loop": false }
          }
        }
        """;

        var manifest = PetManifest.Parse(json, "anim_test");
        byte[] sheetBytes = AssetSource.ReadAllBytes("Assets/pets/neku/spritesheet.png");
        return new SpriteAnimator(manifest, sheetBytes);
    }

    private static PetEngine CreateTestEngine(AppSettings? settings = null, SpriteAnimator? anim = null)
    {
        var view = new DummyPetView();
        var animator = anim ?? CreateAnimator();
        var surfaceProvider = new SurfaceProvider(() => IntPtr.Zero);
        var s = settings ?? new AppSettings();
        var sm = new StateMachine(s);
        return new PetEngine(view, animator, surfaceProvider, sm, s);
    }

    // ── 1. SpriteAnimator and Fallback Tests ───────────────────────────────────

    [Fact]
    public void FallbackChain_contains_standard_expressive_mappings()
    {
        Assert.True(SpriteAnimator.FallbackChain.ContainsKey("wave"));
        Assert.Contains("idle", SpriteAnimator.FallbackChain["wave"]);

        Assert.True(SpriteAnimator.FallbackChain.ContainsKey("celebrate"));
        Assert.Contains("happy", SpriteAnimator.FallbackChain["celebrate"]);

        Assert.True(SpriteAnimator.FallbackChain.ContainsKey("confused"));
        Assert.Contains("idle", SpriteAnimator.FallbackChain["confused"]);
    }

    [Fact]
    public void SpriteAnimator_plays_exact_match_when_present()
    {
        var anim = CreateAnimator();
        anim.Play("wave");
        Assert.Equal("wave", anim.CurrentAnimationName);
        Assert.False(anim.Finished);
    }

    [Fact]
    public void SpriteAnimator_resolves_fallback_when_animation_missing()
    {
        // Manifest has idle and happy, but NOT celebrate
        var anim = CreateAnimator();
        anim.Play("celebrate");

        // "celebrate" fallback chain: ["happy", "jump", "proud", "idle"]
        // Since "happy" is defined in our test manifest, it should select "happy"
        Assert.Equal("happy", anim.CurrentAnimationName);
    }

    [Fact]
    public void SpriteAnimator_resolves_to_idle_for_unmapped_unknown_animation()
    {
        var anim = CreateAnimator();
        anim.Play("completely_unknown_action_xyz");
        Assert.Equal("idle", anim.CurrentAnimationName);
    }

    [Fact]
    public void SpriteAnimator_GetPreviewFrame_returns_valid_image()
    {
        var anim = CreateAnimator();
        var frame = anim.GetPreviewFrame("wave");
        Assert.NotNull(frame);

        var idleFrame = anim.GetPreviewFrame("idle");
        Assert.NotNull(idleFrame);
    }

    // ── 2. PetEngine Contextual Reaction Tests ─────────────────────────────────

    [Fact]
    public void PetEngine_starts_in_idle_state()
    {
        var engine = CreateTestEngine();
        Assert.Equal(PetState.Idle, engine.State);
        Assert.Null(engine.PendingEmotion);
    }

    [Fact]
    public void PetEngine_TriggerReaction_changes_state_and_shows_speech()
    {
        var engine = CreateTestEngine();
        engine.TriggerReaction(PetState.Wave, "Hello world!", cooldownSeconds: 0);

        Assert.Equal(PetState.Wave, engine.State);
        Assert.Equal("Hello world!", engine.SpeechText);
    }

    [Fact]
    public void PetEngine_TriggerReaction_ignores_interruptions_during_drag()
    {
        var engine = CreateTestEngine();
        engine.BeginDrag();
        Assert.Equal(PetState.Drag, engine.State);

        // Attempting to trigger reaction while dragging must be ignored
        engine.TriggerReaction(PetState.Celebrate, "Woohoo!", cooldownSeconds: 0);
        Assert.Equal(PetState.Drag, engine.State);
        Assert.Null(engine.PendingEmotion);
    }

    [Fact]
    public void PetEngine_TriggerReaction_queues_emotion_during_fall()
    {
        var engine = CreateTestEngine();
        engine.BeginDrag();
        engine.EndDrag();
        Assert.Equal(PetState.Fall, engine.State);

        // While falling, reaction must NOT abruptly break physics; it queues into PendingEmotion
        engine.TriggerReaction(PetState.Wave, "Mid-air!", cooldownSeconds: 0);
        Assert.Equal(PetState.Fall, engine.State);
        Assert.Equal(PetState.Wave, engine.PendingEmotion);
    }

    [Fact]
    public void PetEngine_TriggerReaction_enforces_anti_spam_cooldown()
    {
        var engine = CreateTestEngine();
        engine.TriggerReaction(PetState.Wave, "First wave", cooldownSeconds: 5.0);
        Assert.Equal(PetState.Wave, engine.State);

        // Triggering same reaction within cooldown window is ignored
        engine.TriggerReaction(PetState.Wave, "Second wave spam", cooldownSeconds: 5.0);
        Assert.Equal("First wave", engine.SpeechText);
    }

    [Fact]
    public void PetEngine_TriggerReaction_remaps_energetic_reactions_under_reduced_motion()
    {
        var settings = new AppSettings { ReducedMotion = true };
        var engine = CreateTestEngine(settings);

        engine.TriggerReaction(PetState.Celebrate, "High energy!", cooldownSeconds: 0);
        // Under ReducedMotion, Celebrate remaps to Happy
        Assert.Equal(PetState.Happy, engine.State);
    }

    [Fact]
    public void PetEngine_TriggerReaction_remaps_and_silences_under_quiet_mode()
    {
        var engine = CreateTestEngine();
        engine.SetQuiet(true);
        Assert.True(engine.IsQuiet);

        engine.TriggerReaction(PetState.Celebrate, "Loud celebration!", cooldownSeconds: 0);
        // Under Quiet mode, energetic celebration remaps to calm Sit and speech is suppressed
        Assert.Equal(PetState.Sit, engine.State);
        Assert.Equal("", engine.SpeechText);
    }

    [Fact]
    public void PetEngine_TriggerGreeting_triggers_wave_and_polite_speech()
    {
        var engine = CreateTestEngine();
        engine.TriggerGreeting();

        Assert.Equal(PetState.Wave, engine.State);
        Assert.Contains("Good to see you", engine.SpeechText);
    }

    [Fact]
    public void NonLooping_reaction_reverts_to_idle_when_timer_elapses()
    {
        var engine = CreateTestEngine();
        engine.TriggerReaction(PetState.Wave, "Quick wave", cooldownSeconds: 0);
        Assert.Equal(PetState.Wave, engine.State);

        // Advance simulation past wave duration (2.0s)
        engine.Update(1.0);
        Assert.Equal(PetState.Wave, engine.State);

        engine.Update(1.2);
        // Fail-safe transition back to Idle guarantees pet is never stuck
        Assert.Equal(PetState.Idle, engine.State);
    }

    [Fact]
    public void PetEngine_BeginPet_transitions_to_pet_and_leaves_cleanly()
    {
        var engine = CreateTestEngine();
        engine.BeginPet();
        Assert.Equal(PetState.Pet, engine.State);

        engine.EndPet();
        Assert.Equal(PetState.Idle, engine.State);
    }

    [Fact]
    public void DeveloperEventService_triggers_expected_states()
    {
        var settings = new AppSettings { EnableDeveloperEvents = true };

        // Build success -> Celebrate
        var eng1 = CreateTestEngine();
        using var dev1 = new DeveloperEventService(eng1, settings, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        dev1.HandleEvent("build_success", "Build OK");
        Assert.Equal(PetState.Celebrate, eng1.State);

        // Test pass -> Proud
        var eng2 = CreateTestEngine();
        using var dev2 = new DeveloperEventService(eng2, settings, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        dev2.HandleEvent("test_pass", "All green");
        Assert.Equal(PetState.Proud, eng2.State);

        // Build failure -> Annoyed
        var eng3 = CreateTestEngine();
        using var dev3 = new DeveloperEventService(eng3, settings, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        dev3.HandleEvent("build_fail", "Syntax error");
        Assert.Equal(PetState.Annoyed, eng3.State);

        // Test failure -> Surprised
        var eng4 = CreateTestEngine();
        using var dev4 = new DeveloperEventService(eng4, settings, System.Windows.Threading.Dispatcher.CurrentDispatcher);
        dev4.HandleEvent("test_fail", "Assertion failed");
        Assert.Equal(PetState.Surprised, eng4.State);
    }

    [Fact]
    public void Cat_pack_safely_animates_focus_and_break_cycles_using_honest_fallbacks()
    {
        var catManifest = PetManifest.Parse(AssetSource.ReadAllText("Assets/pets/cat/manifest.json"), "Assets/pets/cat/manifest.json");
        var catBytes = AssetSource.ReadAllBytes("Assets/pets/cat/spritesheet.png");

        (PetEngine engine, SpriteAnimator anim) MakeCat()
        {
            var anim = new SpriteAnimator(catManifest, catBytes);
            var eng = CreateTestEngine(anim: anim);
            return (eng, anim);
        }

        // 1. Startup greeting: engine enters Wave, animator falls back to honest feline meow ("talk")
        {
            var (eng, anim) = MakeCat();
            eng.TriggerGreeting();
            Assert.Equal(PetState.Wave, eng.State);
            Assert.Equal("talk", anim.CurrentAnimationName);

            // Advance simulation past greeting duration (2.0s) -> reverts cleanly to Idle
            eng.Update(2.1);
            Assert.Equal(PetState.Idle, eng.State);
            Assert.Equal("idle", anim.CurrentAnimationName);
        }

        // 2. Focus completion: engine enters Celebrate, animator falls back to honest purr ("happy")
        {
            var (eng, anim) = MakeCat();
            eng.TriggerReaction(PetState.Celebrate, "Focus done!", cooldownSeconds: 0);
            Assert.Equal(PetState.Celebrate, eng.State);
            Assert.Equal("happy", anim.CurrentAnimationName);

            eng.Update(2.6);
            Assert.Equal(PetState.Idle, eng.State);
        }

        // 3. Break start: engine enters Stretch, cat has authentic downward stretch
        {
            var (eng, anim) = MakeCat();
            eng.TriggerReaction(PetState.Stretch, "Stretch paws!", cooldownSeconds: 0);
            Assert.Equal(PetState.Stretch, eng.State);
            Assert.Equal("stretch", anim.CurrentAnimationName);
        }

        // 4. Confused reaction: engine enters Confused, animator falls back to curious head tilt ("lookaround")
        {
            var (eng, anim) = MakeCat();
            eng.TriggerReaction(PetState.Confused, "Hmm?", cooldownSeconds: 0);
            Assert.Equal(PetState.Confused, eng.State);
            Assert.Equal("lookaround", anim.CurrentAnimationName);
        }
    }

    [Fact]
    public void Dragging_and_falling_reaction_queueing_and_landing_resumption()
    {
        var engine = CreateTestEngine();
        Assert.Equal(PetState.Idle, engine.State);

        // While dragging, emotions are rejected
        engine.BeginDrag();
        Assert.Equal(PetState.Drag, engine.State);
        engine.TriggerReaction(PetState.Celebrate, "Ignored during drag", cooldownSeconds: 0);
        Assert.Equal(PetState.Drag, engine.State);
        Assert.Null(engine.PendingEmotion);

        // Release drag into free fall
        engine.EndDrag();
        Assert.Equal(PetState.Fall, engine.State);

        // While falling, emotion is queued into PendingEmotion so physics is not interrupted
        engine.TriggerReaction(PetState.Celebrate, "Landing celebration", cooldownSeconds: 0);
        Assert.Equal(PetState.Fall, engine.State);
        Assert.Equal(PetState.Celebrate, engine.PendingEmotion);

        // Simulate falling onto the ground floor
        for (int i = 0; i < 60 && engine.State == PetState.Fall; i++)
        {
            engine.Update(0.016);
        }

        // Upon landing, queued emotion must execute
        Assert.Equal(PetState.Celebrate, engine.State);
        Assert.Null(engine.PendingEmotion);

        // Celebrate elapses and returns cleanly to Idle
        engine.Update(2.6);
        Assert.Equal(PetState.Idle, engine.State);
    }

    [Fact]
    public void Missing_and_unknown_animations_recover_without_getting_stuck()
    {
        var manifest = PetManifest.Parse("""
        {
          "name": "barebones",
          "sheet": "neku.png",
          "cellWidth": 128,
          "cellHeight": 128,
          "columns": 4,
          "scale": 1.0,
          "animations": {
            "idle": { "frames": [0], "fps": 1.0, "loop": true }
          }
        }
        """, "barebones");

        var sheetBytes = AssetSource.ReadAllBytes("Assets/pets/neku/spritesheet.png");
        var anim = new SpriteAnimator(manifest, sheetBytes);

        // Request non-existent animation
        anim.Play("completely_nonexistent_pose_xyz");
        Assert.Equal("idle", anim.CurrentAnimationName);

        // Tick simulation repeatedly: must never crash or throw
        for (int i = 0; i < 10; i++)
        {
            var frame = anim.Tick(0.1);
            Assert.NotNull(frame);
        }
    }
}
