public sealed record RagSearchFilter(
    string? TenantId = null,
    string? Country = null,
    int? Year = null,
    string? Department = null)
{
    public bool Matches(RagDocument document)
    {
        var metadata = document.Metadata;
        if (metadata is null)
            return false;

        if (!string.IsNullOrWhiteSpace(TenantId) &&
            !string.Equals(metadata.TenantId, TenantId, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(Country) &&
            !string.Equals(metadata.Country, Country, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Year.HasValue && metadata.Year != Year.Value)
            return false;

        if (!string.IsNullOrWhiteSpace(Department) &&
            !string.Equals(metadata.Department, Department, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
