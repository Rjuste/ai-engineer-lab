using System.Collections.Concurrent;

public class RagIngestionStatusStore
{
    private readonly ConcurrentDictionary<string, string> _statuses = new();
    private readonly ConcurrentDictionary<string, byte> _claims = new();

    public bool TryClaim(string idempotencyKey)
    {
        return _claims.TryAdd(idempotencyKey, 0);
    }

    public void ReleaseClaim(string idempotencyKey)
    {
        _claims.TryRemove(idempotencyKey, out _);
    }

    public void Set(string idempotencyKey, string status)
    {
        _statuses[idempotencyKey] = status;
    }

    public string? Get(string idempotencyKey)
    {
        return _statuses.TryGetValue(idempotencyKey, out var status)
            ? status
            : null;
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        return new Dictionary<string, string>(_statuses);
    }
}
