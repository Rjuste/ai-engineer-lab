public sealed record JudgeCalibrationCase(
    string Id,
    string Question,
    string Evidence,
    string Answer,
    bool HumanPass,
    string Category);

public sealed record JudgeCalibrationCaseResult(
    string Id,
    string Category,
    bool HumanPass,
    bool JudgePass,
    bool Agreement,
    string Classification,
    double Correctness,
    double Groundedness,
    double Relevance,
    string Reason,
    int TotalTokens);

public sealed record JudgeCalibrationReport(
    int TotalCases,
    int Agreements,
    double AgreementRate,
    int TruePositives,
    int FalsePositives,
    int TrueNegatives,
    int FalseNegatives,
    double Precision,
    double Recall,
    double Specificity,
    int TotalTokens,
    IReadOnlyList<JudgeCalibrationCaseResult> Cases);

public sealed class JudgeCalibrationHarness
{
    private readonly LlmGenerationJudge _judge;

    public JudgeCalibrationHarness(LlmGenerationJudge judge) => _judge = judge;

    public IReadOnlyList<JudgeCalibrationCase> Dataset { get; } =
    [
        new("pto-numeric-exact", "What is the maximum PTO balance?", "Unused PTO may carry over up to a maximum accumulated balance of 25 days.", "The maximum PTO balance is 25 days.", true, "clear-pass"),
        new("pto-semantic-equivalent", "What is the maximum PTO balance?", "Unused PTO may carry over up to a maximum accumulated balance of 25 days.", "Employees can accumulate at most twenty-five days of PTO.", true, "semantic-pass"),
        new("pto-wrong-number", "What is the maximum PTO balance?", "Unused PTO may carry over up to a maximum accumulated balance of 25 days.", "The maximum PTO balance is 30 days.", false, "clear-fail"),
        new("pto-unsupported-extra", "What is the maximum PTO balance?", "Unused PTO may carry over up to a maximum accumulated balance of 25 days.", "The maximum balance is 25 days, and extra PTO is automatically paid out in cash.", false, "hallucination"),
        new("payment-supported", "What happens when invoice payment processing times out?", "If the payment processor times out, the invoice remains pending and the payment may be retried according to retry policy.", "The invoice remains pending and the payment may be retried under the retry policy.", true, "clear-pass"),
        new("payment-invented-refund", "What happens when invoice payment processing times out?", "If the payment processor times out, the invoice remains pending and the payment may be retried according to retry policy.", "The invoice is automatically refunded and permanently cancelled.", false, "hallucination")
    ];

    public async Task<JudgeCalibrationReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<JudgeCalibrationCaseResult>();
        foreach (var item in Dataset)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var judged = await _judge.EvaluateAsync(new LlmJudgeRequest(item.Question, item.Evidence, item.Answer), cancellationToken);
            var classification = (item.HumanPass, judged.Passed) switch
            {
                (true, true) => "TP",
                (false, true) => "FP",
                (false, false) => "TN",
                (true, false) => "FN"
            };
            results.Add(new JudgeCalibrationCaseResult(item.Id, item.Category, item.HumanPass, judged.Passed, item.HumanPass == judged.Passed, classification, judged.Correctness, judged.Groundedness, judged.Relevance, judged.Reason, judged.TotalTokens));
        }

        var tp = results.Count(x => x.Classification == "TP");
        var fp = results.Count(x => x.Classification == "FP");
        var tn = results.Count(x => x.Classification == "TN");
        var fn = results.Count(x => x.Classification == "FN");
        var agreements = tp + tn;
        return new JudgeCalibrationReport(
            results.Count,
            agreements,
            Ratio(agreements, results.Count),
            tp, fp, tn, fn,
            Ratio(tp, tp + fp),
            Ratio(tp, tp + fn),
            Ratio(tn, tn + fp),
            results.Sum(x => x.TotalTokens),
            results);
    }

    private static double Ratio(int numerator, int denominator) => denominator == 0 ? 0 : (double)numerator / denominator;
}
