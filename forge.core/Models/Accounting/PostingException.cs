namespace Forge.Core.Models.Accounting;

/// <summary>
/// A hard, alertable posting failure (§5.2): unbalanced entry, unmapped /
/// non-postable / cross-book determination key, control line missing a party,
/// a post into a HardClosed (or non-overridden SoftClosed) period, a reversal
/// precondition violation, etc. A posting failure fails the operation
/// immediately and visibly — it is never silently swallowed.
/// </summary>
public class PostingException : Exception
{
    /// <summary>Machine-readable reason code (e.g. <c>UNBALANCED</c>, <c>PERIOD_HARD_CLOSED</c>).</summary>
    public string Code { get; }

    public PostingException(string code, string message) : base(message)
    {
        Code = code;
    }

    public PostingException(string code, string message, Exception inner) : base(message, inner)
    {
        Code = code;
    }
}
