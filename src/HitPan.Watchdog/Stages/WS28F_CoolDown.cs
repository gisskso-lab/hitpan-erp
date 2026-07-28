using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace HitPan.Watchdog.Stages;

public class WS28F_CoolDown
{
    private readonly ILogger<WS28F_CoolDown> _logger;
    private readonly WatchdogOptions _options;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _history = new();
    private readonly object _lock = new();

    public WS28F_CoolDown(ILogger<WS28F_CoolDown> logger, IOptions<WatchdogOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public int RecentRecoveryCount
    {
        get
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddHours(-1);
                int total = 0;
                foreach (var q in _history.Values)
                {
                    while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
                    total += q.Count;
                }
                return total;
            }
        }
    }

    public bool AllowRecovery(string key)
    {
        lock (_lock)
        {
            var q = _history.GetOrAdd(key, _ => new Queue<DateTime>());
            var cutoff = DateTime.UtcNow.AddHours(-1);
            while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
            if (q.Count >= _options.CoolDownMaxPerHour)
            {
                _logger.LogError("WS-28-F: cool down exceeded for {Key} ({Count}/hour)", key, q.Count);
                return false;
            }
            q.Enqueue(DateTime.UtcNow);
            _logger.LogWarning("WS-28-F: recovery allowed for {Key} ({Count}/hour)", key, q.Count);
            return true;
        }
    }
}
