public class TokenEstimator
{
    public int Estimate(IReadOnlyList<LlmMessage> messages)
    {
        var characterCount = messages.Sum(message => message.Content.Length);
        var messageOverhead = messages.Count * 4;

        return Math.Max(1, (int)Math.Ceiling(characterCount / 4.0) + messageOverhead);
    }
}
