using System.Collections.Concurrent;

public class RagIngestionStatusStore
{
    private readonly ConcurrentDictionary<string, string> _statuses = new();

    public void Set(string documentId, string status)
    {
        _statuses[documentId] = status;
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        return new Dictionary<string, string>(_statuses);
    }
}
