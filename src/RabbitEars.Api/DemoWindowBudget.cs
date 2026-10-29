namespace RabbitEars.Api;

/// <summary>A bounded, expiring address budget. Full tables refuse new keys rather than growing.</summary>
public sealed class DemoWindowBudget
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> _windows = [];
    public bool TryTake(string key, DateTimeOffset now, int limit)
    {
        lock (_gate)
        {
            foreach (var stale in _windows.Where(p => now - p.Value.Start >= TimeSpan.FromMinutes(1)).Select(p => p.Key).ToArray())
                _windows.Remove(stale);
            if (!_windows.TryGetValue(key, out var window))
            {
                if (_windows.Count >= 1024) return false;
                window = (now, 0);
            }
            if (window.Count >= limit) return false;
            _windows[key] = (window.Start, window.Count + 1);
            return true;
        }
    }
}
