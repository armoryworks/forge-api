namespace Forge.Api.Capabilities.Discovery;

/// <summary>
/// Phase 4 Phase-F — One captured answer to a discovery question. The
/// <see cref="Value"/> is the raw form value: a choice key for single-choice,
/// a comma-joined list for multi-choice, the literal "yes"/"no" for yes/no,
/// or arbitrary free text. No structured parsing — questions decide what to
/// do with their answer in <see cref="DiscoveryRecommendationEngine"/>.
/// </summary>
public record DiscoveryAnswer(string QuestionId, string Value);

/// <summary>
/// Phase 4 Phase-F — The full answer set for a single discovery walkthrough.
/// Convenience helpers below extract the typed values the recommendation
/// engine needs without leaking dictionary lookups everywhere.
/// </summary>
public class DiscoveryAnswerSet
{
    private readonly Dictionary<string, string> _byId;

    public DiscoveryAnswerSet(IEnumerable<DiscoveryAnswer> answers)
    {
        _byId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in answers)
        {
            if (string.IsNullOrEmpty(a.QuestionId)) continue;
            _byId[a.QuestionId] = a.Value ?? string.Empty;
        }
    }

    public IReadOnlyDictionary<string, string> Raw => _byId;

    public string? Get(string id) => _byId.TryGetValue(id, out var v) ? v : null;

    public bool TryGet(string id, out string value)
    {
        if (_byId.TryGetValue(id, out var v))
        {
            value = v;
            return true;
        }
        value = string.Empty;
        return false;
    }

    public bool IsAnswered(string id) => _byId.ContainsKey(id);

    /// <summary>Q-O1 → headcount-bucket variable. Returns "small" / "small-mid" / "mid" / "large".</summary>
    public string HeadcountBucket
    {
        get
        {
            var raw = Get("Q-O1") ?? string.Empty;
            return raw switch
            {
                "1-2" => "small",
                "3-10" or "11-25" => "small-mid",
                "26-50" or "51-200" => "mid",
                "200+" => "large",
                _ => "small-mid", // conservative default
            };
        }
    }

    /// <summary>Q-O3 → mode variable. Returns "production" / "distribution" / "hybrid".</summary>
    public string Mode
    {
        get
        {
            var raw = Get("Q-O3") ?? string.Empty;
            return raw switch
            {
                "make" => "production",
                "resell" => "distribution",
                "both" => "hybrid",
                _ => "production",
            };
        }
    }

    /// <summary>Q-O4 → regulated flag. true unless "no" or unanswered.</summary>
    public bool Regulated
    {
        get
        {
            var raw = Get("Q-O4") ?? string.Empty;
            return !string.IsNullOrEmpty(raw) && !string.Equals(raw, "no", StringComparison.Ordinal);
        }
    }

    /// <summary>Q-O5 → sites variable. "single" / "dual" / "multi".</summary>
    public string Sites
    {
        get
        {
            var raw = Get("Q-O5") ?? string.Empty;
            return raw switch
            {
                "1" => "single",
                "2" => "dual",
                "3+" => "multi",
                _ => "single",
            };
        }
    }

    /// <summary>
    /// Pro Services rollout D4 — Q-S1 → business-type variable.
    /// Returns "products" / "services" / "both". When Q-S1 is unanswered
    /// the value defaults to "products" so the existing manufacturing-
    /// flavored 22-question path runs (backward compatible).
    /// </summary>
    public string BusinessType
    {
        get
        {
            var raw = Get("Q-S1") ?? string.Empty;
            return raw switch
            {
                "services" => "services",
                "both"     => "both",
                _          => "products",
            };
        }
    }
}
