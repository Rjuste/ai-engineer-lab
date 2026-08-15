public class DocumentChunker
{
    private const int ChunkSizeWords = 18;
    private const int OverlapWords = 4;

    public IReadOnlyList<RagDocument> Chunk(RagDocument document)
    {
        var words = document.Content
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<RagDocument>();
        var step = ChunkSizeWords - OverlapWords;
        var chunkIndex = 0;

        for (var start = 0; start < words.Length; start += step)
        {
            var chunkWords = words.Skip(start).Take(ChunkSizeWords).ToArray();
            if (chunkWords.Length == 0)
                break;

            chunks.Add(new RagDocument(
                $"{document.Id}-chunk-{chunkIndex}",
                string.Join(' ', chunkWords),
                document.Metadata));

            chunkIndex++;

            if (start + ChunkSizeWords >= words.Length)
                break;
        }

        return chunks;
    }
}
