using System.Threading.Channels;

public class RagIngestionQueue
{
    private readonly Channel<RagDocument> _channel = Channel.CreateUnbounded<RagDocument>();

    public ValueTask EnqueueAsync(
        RagDocument document,
        CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(document, cancellationToken);
    }

    public IAsyncEnumerable<RagDocument> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
