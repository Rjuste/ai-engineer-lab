using System.Threading.Channels;

public class RagIngestionQueue
{
    private readonly Channel<RagIngestionJob> _channel =
        Channel.CreateUnbounded<RagIngestionJob>();

    public ValueTask EnqueueAsync(
        RagIngestionJob job,
        CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public IAsyncEnumerable<RagIngestionJob> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
