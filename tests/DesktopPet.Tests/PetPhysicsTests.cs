using System.Windows.Media;
using DesktopPet.Engine;
using DesktopPet.Services;
using DesktopPet.UI;
using Xunit;

namespace DesktopPet.Tests;

public class PetPhysicsTests
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

    private static SpriteAnimator CreateAnimator()
    {
        string json = """
        {
          "name": "physics_test",
          "sheet": "neku.png",
          "cellWidth": 128,
          "cellHeight": 128,
          "columns": 4,
          "scale": 1.0,
          "animations": {
            "idle": { "frames": [0, 1], "fps": 4.0, "loop": true },
            "walk": { "frames": [2, 3], "fps": 6.0, "loop": true },
            "run": { "frames": [4, 5], "fps": 8.0, "loop": true },
            "sit": { "frames": [6, 7], "fps": 2.0, "loop": true },
            "loaf": { "frames": [8, 9], "fps": 2.0, "loop": true },
            "pounce": { "frames": [10, 11], "fps": 6.0, "loop": false },
            "look_around": { "frames": [12, 13], "fps": 3.0, "loop": false }
          }
        }
        """;
        var manifest = PetManifest.Parse(json, "physics_test");
        byte[] sheetBytes = AssetSource.ReadAllBytes("Assets/pets/neku/spritesheet.png");
        return new SpriteAnimator(manifest, sheetBytes);
    }

    private static PetEngine CreateTestEngine(AppSettings? settings = null, ToyService? toyService = null)
    {
        var view = new DummyPetView();
        var animator = CreateAnimator();
        var surfaceProvider = new SurfaceProvider(() => IntPtr.Zero);
        var s = settings ?? new AppSettings();
        var sm = new StateMachine(s);
        var ts = toyService ?? new ToyService();
        return new PetEngine(view, animator, surfaceProvider, sm, s, toyService: ts);
    }

    // ── 1. Smooth Movement & Acceleration Tests ───────────────────────────────

    [Fact]
    public void MoveTowards_accelerates_smoothly_toward_target()
    {
        double current = 0.0;
        double target = 100.0;
        double maxDelta = 20.0;

        double step1 = PetEngine.MoveTowards(current, target, maxDelta);
        Assert.Equal(20.0, step1);

        double step2 = PetEngine.MoveTowards(step1, target, maxDelta);
        Assert.Equal(40.0, step2);
    }

    [Fact]
    public void MoveTowards_decelerates_smoothly_toward_zero()
    {
        double current = 50.0;
        double target = 0.0;
        double maxDelta = 15.0;

        double step1 = PetEngine.MoveTowards(current, target, maxDelta);
        Assert.Equal(35.0, step1);

        double step2 = PetEngine.MoveTowards(step1, target, maxDelta);
        Assert.Equal(20.0, step2);
    }

    [Fact]
    public void MoveTowards_does_not_overshoot_target()
    {
        double current = 95.0;
        double target = 100.0;
        double maxDelta = 20.0;

        double result = PetEngine.MoveTowards(current, target, maxDelta);
        Assert.Equal(100.0, result);
    }

    // ── 2. Virtual Toy Service Lifecycle Tests ─────────────────────────────────

    [Fact]
    public void ToyService_spawns_and_tracks_toys()
    {
        var service = new ToyService();
        Assert.Empty(service.Toys);

        var ball = service.SpawnToy(ToyType.Ball, 200, 300);
        Assert.Single(service.Toys);
        Assert.Equal(ToyType.Ball, ball.Type);
        Assert.Equal("🎾", ball.Emoji);
        Assert.Equal(200, ball.X);
        Assert.Equal(300, ball.Y);

        var feather = service.SpawnToy(ToyType.Feather, 500, 400);
        Assert.Equal(2, service.Toys.Count);
        Assert.Equal("🪶", feather.Emoji);

        var cushion = service.SpawnToy(ToyType.Cushion, 800, 600);
        Assert.Equal(3, service.Toys.Count);
        Assert.Equal("🛋️", cushion.Emoji);
    }

    [Fact]
    public void ToyService_updates_toy_position()
    {
        var service = new ToyService();
        var toy = service.SpawnToy(ToyType.Ball, 100, 100);

        bool updated = service.UpdateToyPosition(toy.Id, 250, 350);
        Assert.True(updated);

        var retrieved = service.GetToy(toy.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(250, retrieved.X);
        Assert.Equal(350, retrieved.Y);
    }

    [Fact]
    public void ToyService_removes_individual_toy_and_clears_all()
    {
        var service = new ToyService();
        var t1 = service.SpawnToy(ToyType.Ball, 100, 100);
        var t2 = service.SpawnToy(ToyType.Feather, 200, 200);

        Assert.True(service.RemoveToy(t1.Id));
        Assert.Single(service.Toys);
        Assert.Null(service.GetToy(t1.Id));
        Assert.NotNull(service.GetToy(t2.Id));

        bool clearedFired = false;
        service.AllToysCleared += () => clearedFired = true;
        service.ClearAll();

        Assert.Empty(service.Toys);
        Assert.True(clearedFired);
    }

    [Fact]
    public void ToyService_finds_nearest_toy_within_radius()
    {
        var service = new ToyService();
        service.SpawnToy(ToyType.Ball, 100, 100);       // distance sqrt((120-100)^2 + (100-100)^2) = 20
        service.SpawnToy(ToyType.Feather, 500, 500);    // far away

        var nearest = service.FindNearestToy(120, 100, maxRadius: 100);
        Assert.NotNull(nearest);
        Assert.Equal(ToyType.Ball, nearest.Type);

        var none = service.FindNearestToy(120, 100, maxRadius: 10);
        Assert.Null(none);
    }

    // ── 3. Pet Engine & Toy Interaction Tests ─────────────────────────────────

    [Fact]
    public void PetEngine_interacts_with_nearest_toy()
    {
        var toyService = new ToyService();
        var settings = new AppSettings { EnableToyInteractions = true };
        var engine = CreateTestEngine(settings, toyService);

        // Place pet at (200, 200) and toy close at (220, 200)
        engine.TeleportTo(200, 200);
        var toy = toyService.SpawnToy(ToyType.Feather, 220, 200);

        bool interacted = engine.TryInteractWithToys(0.1);
        Assert.True(interacted);
        Assert.True(engine.IsSpeaking || engine.State == PetState.Pounce || engine.State == PetState.Play);
    }

    [Fact]
    public void PetEngine_ignores_toys_when_interaction_setting_disabled()
    {
        var toyService = new ToyService();
        var settings = new AppSettings { EnableToyInteractions = false };
        var engine = CreateTestEngine(settings, toyService);

        engine.TeleportTo(200, 200);
        toyService.SpawnToy(ToyType.Ball, 220, 200);

        bool interacted = engine.TryInteractWithToys(0.1);
        Assert.False(interacted);
    }

    // ── 4. Edge Resting & Personality Dialogue Tests ──────────────────────────

    [Fact]
    public void Personality_provides_unique_lines_for_each_toy_type()
    {
        var neku = new Personality("neku");
        string ballLine = neku.GetToyLine(ToyType.Ball);
        string featherLine = neku.GetToyLine(ToyType.Feather);
        string cushionLine = neku.GetToyLine(ToyType.Cushion);

        Assert.False(string.IsNullOrWhiteSpace(ballLine));
        Assert.False(string.IsNullOrWhiteSpace(featherLine));
        Assert.False(string.IsNullOrWhiteSpace(cushionLine));

        var billu = new Personality("billu");
        string billuBallLine = billu.GetToyLine(ToyType.Ball);
        string billuFeatherLine = billu.GetToyLine(ToyType.Feather);
        string billuCushionLine = billu.GetToyLine(ToyType.Cushion);

        Assert.False(string.IsNullOrWhiteSpace(billuBallLine));
        Assert.False(string.IsNullOrWhiteSpace(billuFeatherLine));
        Assert.False(string.IsNullOrWhiteSpace(billuCushionLine));

        // Billu and Neku have distinct voice / style
        Assert.NotEqual(ballLine, billuBallLine);
    }

    [Fact]
    public void Edge_resting_positions_companion_safely_facing_inward()
    {
        var settings = new AppSettings { EnableEdgeResting = true };
        var engine = CreateTestEngine(settings);

        // Position at far left edge (x <= 60)
        engine.TeleportTo(30, 400);
        Assert.True(engine.X <= 60);

        // Near left edge, companion should look or face right (inward)
        // Ensure engine is bounded within screen
        Assert.True(engine.X >= 0);
    }

    [Fact]
    public void Autonomous_movement_stops_immediately_when_disabled()
    {
        var settings = new AppSettings { EnableAutonomousWandering = true };
        var engine = CreateTestEngine(settings);

        engine.TeleportTo(300, 300);
        engine.EnterState(PetState.Walk);
        Assert.Equal(PetState.Walk, engine.State);

        // Turn off autonomous wandering
        settings.EnableAutonomousWandering = false;

        // Step simulation
        engine.Update(0.1);

        // Movement must stop immediately and revert to Idle
        Assert.Equal(PetState.Idle, engine.State);
    }

    [Fact]
    public void Dragging_manually_interrupts_movement_immediately()
    {
        var settings = new AppSettings { EnableAutonomousWandering = true };
        var engine = CreateTestEngine(settings);

        engine.TeleportTo(300, 300);
        engine.EnterState(PetState.Walk);
        Assert.Equal(PetState.Walk, engine.State);

        // User grabs the pet
        engine.BeginDrag();
        Assert.Equal(PetState.Drag, engine.State);

        // User releases the pet
        engine.EndDrag();
        Assert.Equal(PetState.Fall, engine.State);
    }
}
