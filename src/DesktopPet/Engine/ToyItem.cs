namespace DesktopPet.Engine;

/// <summary>
/// Supported types of interactive virtual toys.
/// </summary>
public enum ToyType
{
    /// <summary>Bouncy ball that rolls, bounces, and invites batting or chasing.</summary>
    Ball,
    /// <summary>Light feather wand that flutters and inspires leaping or stalking.</summary>
    Feather,
    /// <summary>Soft resting cushion that invites cozy naps and loafing.</summary>
    Cushion
}

/// <summary>
/// Represents an active virtual desktop toy placed in screen coordinates (DIPs).
/// </summary>
public sealed class ToyItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ToyType Type { get; init; } = ToyType.Ball;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 48;
    public double Height { get; set; } = 48;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public int InteractionCount { get; set; }

    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public string DisplayName => Type switch
    {
        ToyType.Ball => "Bouncy Ball",
        ToyType.Feather => "Feather Wand",
        ToyType.Cushion => "Plush Cushion",
        _ => "Toy"
    };

    public string Emoji => Type switch
    {
        ToyType.Ball => "🎾",
        ToyType.Feather => "🪶",
        ToyType.Cushion => "🛋️",
        _ => "🧸"
    };
}
