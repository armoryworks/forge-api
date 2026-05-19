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

    /// <summary>
    /// Q-O3 → presence-of-make. Q-O3 is multi-choice with comma-joined
    /// values; the legacy single-choice "make" / "both" answers also match
    /// (backward compat — pre-multi-select sessions still resolve correctly).
    /// </summary>
    public bool HasMake => HasQO3Selection("make") || HasQO3Selection("both");

    /// <summary>Q-O3 → presence-of-resell. Same compat as <see cref="HasMake"/>.</summary>
    public bool HasResell => HasQO3Selection("resell") || HasQO3Selection("both");

    /// <summary>
    /// Q-O3 → presence-of-services. Only the multi-choice era exposes this;
    /// services was previously expressed only via Q-S1.
    /// </summary>
    public bool HasServices => HasQO3Selection("services");

    /// <summary>
    /// True when Q-O3 has services selected AND no product axis (make/resell).
    /// This is THE signal worth a preset reorientation — services-as-the-
    /// whole-business. Services + make/resell is just "we sell some services
    /// as line items" and routes through the existing manufacturing flow.
    /// </summary>
    public bool ServicesOnly => HasServices && !HasMake && !HasResell;

    /// <summary>
    /// Q-O3 → mode variable for the manufacturing-flow downstream routing.
    /// "production" / "distribution" / "hybrid". Services presence is
    /// handled separately by the engine (see <see cref="ServicesOnly"/>);
    /// this property only cares about the make/resell axis.
    /// </summary>
    public string Mode
    {
        get
        {
            if (HasMake && HasResell) return "hybrid";
            if (HasMake) return "production";
            if (HasResell) return "distribution";

            // Legacy single-choice fallback. Sessions captured before the
            // multi-choice migration stored raw "make" / "resell" / "both".
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

    private bool HasQO3Selection(string key)
    {
        var raw = Get("Q-O3");
        if (string.IsNullOrEmpty(raw)) return false;
        // Multi-choice values are comma-joined. Tolerate whitespace around
        // commas in case future clients send a "make, resell" shape.
        foreach (var token in raw.Split(','))
        {
            if (string.Equals(token.Trim(), key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Q-O4 → regulated flag. True when ANY cert selection is present
    /// (medical / aerospace / automotive / food / pharma / other). The
    /// explicit "no, none of these apply" answer (or unanswered) → false.
    ///
    /// Multi-choice contract: comma-joined values. A user who picks both
    /// "no" and a cert (contradictory input) is treated as regulated —
    /// any cert wins over "no" because under-recommending the Regulated
    /// preset is the worse failure mode than over-recommending it.
    /// </summary>
    public bool Regulated => Regulations.Count > 0;

    /// <summary>
    /// Q-O4 → list of selected regulation/cert tokens (medical, aerospace,
    /// automotive, food, pharma, other). Empty when only "no" or
    /// unanswered. Multi-choice can return multiple — businesses serving
    /// multiple regulated markets (medical + aerospace, food + pharma) are
    /// common.
    /// </summary>
    public IReadOnlyList<string> Regulations
    {
        get
        {
            var raw = Get("Q-O4");
            if (string.IsNullOrEmpty(raw)) return Array.Empty<string>();

            var list = new List<string>();
            foreach (var token in raw.Split(','))
            {
                var t = token.Trim();
                if (t.Length == 0) continue;
                if (string.Equals(t, "no", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(t);
            }
            return list;
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
