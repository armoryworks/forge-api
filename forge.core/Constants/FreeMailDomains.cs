using System.Collections.Frozen;

namespace Forge.Core.Constants;

/// <summary>
/// Consumer mailbox providers, which may never back a domain-wildcard ingest rule.
///
/// <para><b>Why this is compiled in rather than configurable.</b> A domain rule
/// says "everyone who mails from here is this customer". On a company domain
/// that is roughly true. On gmail.com it attaches every unrelated sender on
/// earth to one customer record — and because the retail lane and the
/// proof-of-intent lane both key authorization off party identity, a bad domain
/// rule does not merely misfile mail, it manufactures evidence that a customer
/// authorized something. That is not a mistake an install should be able to
/// make by editing a settings row at 2am, so the list is not in
/// <c>reference_data</c> and there is no override flag.</para>
///
/// <para>Individual addresses at these domains are still perfectly usable —
/// a sole trader whose business address is a Gmail account gets an
/// <see cref="Enums.IngestRuleMatchType.Address"/> rule, which is exact and safe.
/// Only the wildcard is refused.</para>
///
/// <para>The list is deliberately not exhaustive; it cannot be. It covers the
/// providers common enough that someone would plausibly type one in. Anything
/// missed is caught by the reviewer, because a domain match can never exceed
/// <see cref="Enums.CommunicationMatchConfidence.Domain"/> and so can never
/// reach the draft-order path on its own.</para>
/// </summary>
public static class FreeMailDomains
{
    private static readonly FrozenSet<string> Blocked = new[]
    {
        // Global majors
        "gmail.com", "googlemail.com",
        "yahoo.com", "yahoo.co.uk", "yahoo.co.jp", "ymail.com", "rocketmail.com",
        "outlook.com", "hotmail.com", "hotmail.co.uk", "live.com", "msn.com", "passport.com",
        "aol.com", "aim.com",
        "icloud.com", "me.com", "mac.com",
        "proton.me", "protonmail.com", "pm.me",
        "zoho.com", "zohomail.com",
        "gmx.com", "gmx.net", "gmx.de",
        "mail.com", "email.com", "usa.com",
        "yandex.com", "yandex.ru",
        "fastmail.com", "fastmail.fm",
        "hushmail.com", "tutanota.com", "tuta.com",

        // ISP mailboxes — same problem, still handed out with the connection
        "comcast.net", "verizon.net", "att.net", "sbcglobal.net", "bellsouth.net",
        "cox.net", "charter.net", "earthlink.net", "juno.com", "netzero.net",
        "shaw.ca", "rogers.com", "sympatico.ca", "telus.net",
        "btinternet.com", "sky.com", "virginmedia.com", "talktalk.net",

        // Disposable / throwaway — never a business counterparty
        "mailinator.com", "guerrillamail.com", "10minutemail.com", "yopmail.com",
        "temp-mail.org", "throwawaymail.com", "sharklasers.com", "trashmail.com",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="domain"/> is a consumer mailbox provider and so
    /// cannot back a domain-wildcard rule. Accepts a bare domain or a full
    /// address; anything after the last '@' is taken as the domain.
    /// </summary>
    public static bool IsFreeMail(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;

        var value = domain.Trim().TrimEnd('.');
        var at = value.LastIndexOf('@');
        if (at >= 0) value = value[(at + 1)..];

        return Blocked.Contains(value);
    }

    /// <summary>Exposed for the admin UI to explain the refusal, and for the test that pins the contract.</summary>
    public static IReadOnlySet<string> All => Blocked;
}
