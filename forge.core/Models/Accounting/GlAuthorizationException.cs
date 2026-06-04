using Forge.Core.Enums.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>
/// A GL segregation-of-duties denial at the <see cref="Forge.Core.Interfaces.IPostingEngine"/>
/// boundary (ACCOUNTING_SUITE_PLAN §5.7): the current principal does not
/// effectively hold the GL capability required to drive the requested
/// operation. Distinct from <see cref="PostingException"/> (which is a
/// data/validation failure) so the HTTP edge can map it to 403 rather than 400.
/// </summary>
public class GlAuthorizationException : Exception
{
    /// <summary>The GL capability the caller was missing.</summary>
    public GlCapability RequiredCapability { get; }

    public GlAuthorizationException(GlCapability requiredCapability, string message)
        : base(message)
    {
        RequiredCapability = requiredCapability;
    }
}
