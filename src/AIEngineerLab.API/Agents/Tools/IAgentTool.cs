public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    object Parameters { get; }

    Task<string> ExecuteAsync(
        string argumentsJson,
        CancellationToken cancellationToken = default);
}
