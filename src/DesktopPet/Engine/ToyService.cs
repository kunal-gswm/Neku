namespace DesktopPet.Engine;

/// <summary>
/// Service managing interactive virtual toys placed on the user's desktop.
/// Fully local-first and testable without UI dependencies.
/// </summary>
public sealed class ToyService
{
    private readonly List<ToyItem> _toys = new();
    private readonly object _lock = new();

    public IReadOnlyList<ToyItem> ActiveToys
    {
        get
        {
            lock (_lock)
            {
                return _toys.ToList();
            }
        }
    }

    public IReadOnlyList<ToyItem> Toys => ActiveToys;
    public ToyItem? GetToy(string toyId) => ActiveToys.FirstOrDefault(t => t.Id == toyId);
    public ToyItem? FindNearestToy(double x, double y, double maxRadius = 1200) => GetNearestToy(x, y, maxRadius);

    public event Action<ToyItem>? ToySpawned;
    public event Action<ToyItem>? ToyRemoved;
    public event Action<ToyItem>? ToyUpdated;
    public event Action? AllToysCleared;

    /// <summary>
    /// Spawns a new toy at specified DIP coordinates.
    /// </summary>
    public ToyItem SpawnToy(ToyType type, double x, double y, double width = 48, double height = 48)
    {
        var toy = new ToyItem
        {
            Type = type,
            X = x,
            Y = y,
            Width = width,
            Height = height
        };

        lock (_lock)
        {
            _toys.Add(toy);
        }

        ToySpawned?.Invoke(toy);
        return toy;
    }

    /// <summary>
    /// Removes a toy by its unique ID.
    /// </summary>
    public bool RemoveToy(string toyId)
    {
        ToyItem? item = null;
        lock (_lock)
        {
            int index = _toys.FindIndex(t => t.Id == toyId);
            if (index >= 0)
            {
                item = _toys[index];
                _toys.RemoveAt(index);
            }
        }

        if (item != null)
        {
            ToyRemoved?.Invoke(item);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Updates position of an existing toy (e.g. while being dragged).
    /// </summary>
    public bool UpdateToyPosition(string toyId, double x, double y)
    {
        ToyItem? item = null;
        lock (_lock)
        {
            item = _toys.FirstOrDefault(t => t.Id == toyId);
            if (item != null)
            {
                item.X = x;
                item.Y = y;
            }
        }

        if (item != null)
        {
            ToyUpdated?.Invoke(item);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Increments the interaction count for a toy when the pet engages with it.
    /// </summary>
    public void RecordInteraction(string toyId)
    {
        ToyItem? item = null;
        lock (_lock)
        {
            item = _toys.FirstOrDefault(t => t.Id == toyId);
            if (item != null)
            {
                item.InteractionCount++;
            }
        }

        if (item != null)
        {
            ToyUpdated?.Invoke(item);
        }
    }

    /// <summary>
    /// Removes all active toys.
    /// </summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _toys.Clear();
        }
        AllToysCleared?.Invoke();
    }

    /// <summary>
    /// Finds the nearest active toy to the given point within maxDistance.
    /// </summary>
    public ToyItem? GetNearestToy(double x, double y, double maxDistance = 1200)
    {
        lock (_lock)
        {
            ToyItem? nearest = null;
            double minDistanceSq = maxDistance * maxDistance;

            foreach (var toy in _toys)
            {
                double dx = toy.CenterX - x;
                double dy = toy.CenterY - y;
                double distSq = dx * dx + dy * dy;

                if (distSq < minDistanceSq)
                {
                    minDistanceSq = distSq;
                    nearest = toy;
                }
            }

            return nearest;
        }
    }
}
