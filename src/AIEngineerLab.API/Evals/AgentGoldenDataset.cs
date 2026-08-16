public static class AgentGoldenDataset
{
    public static IReadOnlyList<AgentEvalExpectation> Cases { get; } =
    [
        new(
            Id: "division-tool-selection",
            UserRequest: "Use the division tool to divide 84 by 7.",
            ExpectedTool: "divide_numbers",
            ExpectedArguments: new Dictionary<string, string>
            {
                ["numerator"] = "84",
                ["denominator"] = "7"
            },
            ForbiddenTools: ["search_knowledge_base", "get_rag_status"],
            MaxToolExecutions: 1),

        new(
            Id: "rag-status-selection",
            UserRequest: "Is the RAG index ready and how many vectors are indexed?",
            ExpectedTool: "get_rag_status",
            ForbiddenTools: ["divide_numbers", "search_knowledge_base"],
            MaxToolExecutions: 1),

        new(
            Id: "knowledge-search-selection",
            UserRequest: "According to our internal documentation, what is the current PTO carryover policy?",
            ExpectedTool: "search_knowledge_base",
            ForbiddenTools: ["divide_numbers", "get_rag_status"],
            MaxToolExecutions: 1),

        new(
            Id: "no-tool-conceptual",
            UserRequest: "Explain in general terms what an embedding is.",
            ExpectedTool: null,
            ForbiddenTools: ["divide_numbers", "get_rag_status", "search_knowledge_base"],
            MaxToolExecutions: 0)
    ];
}
