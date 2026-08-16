namespace Forge.Core.Enums;

/// <summary>How an ingest rule decides whether a message is in scope.</summary>
public enum IngestRuleMatchType
{
    /// <summary>One exact mailbox. Highest precision; matches yield Exact confidence.</summary>
    Address,

    /// <summary>
    /// Every sender at a domain. Yields Domain confidence only. Free-mail
    /// domains are refused for this type — see <c>FreeMailDomains</c>.
    /// </summary>
    Domain,
}
