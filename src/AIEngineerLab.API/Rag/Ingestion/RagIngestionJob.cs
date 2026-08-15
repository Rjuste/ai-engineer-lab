public record RagIngestionJob(RagDocument Document, int Version, int Attempt = 1)
{
    public string IdempotencyKey => $"{Document.Id}:{Version}";
}
