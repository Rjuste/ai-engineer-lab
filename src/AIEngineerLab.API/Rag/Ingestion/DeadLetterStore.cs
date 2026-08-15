using System.Collections.Concurrent;

public record DeadLetterEntry(
    string IdempotencyKey,
    string DocumentId,
    int Version,
    int Attempts,
    string Error);

public class DeadLetterStore
{
    private readonly ConcurrentQueue<DeadLetterEntry> _entries = new();

    public void Add(DeadLetterEntry entry) => _entries.Enqueue(entry);

    public IReadOnlyList<DeadLetterEntry> GetAll() => _entries.ToArray();
}
