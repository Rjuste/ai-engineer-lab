public sealed class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(
            tool => tool.Name,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<LlmToolDefinition> GetDefinitions()
    {
        return _tools.Values
            .Select(tool => new LlmToolDefinition(
                tool.Name,
                tool.Description,
                tool.Parameters))
            .ToList();
    }

    public bool TryGet(string name, out IAgentTool tool)
    {
        return _tools.TryGetValue(name, out tool!);
    }
}
