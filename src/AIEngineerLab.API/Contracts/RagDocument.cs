public record RagMetadata(
    string TenantId = "tenant-123",
    string Country = "US",
    int Year = 2026,
    string Department = "Engineering");

public record RagDocument(
    string Id,
    string Content,
    RagMetadata? Metadata = null);
