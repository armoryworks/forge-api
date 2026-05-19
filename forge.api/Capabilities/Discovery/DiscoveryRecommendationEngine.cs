using System.Globalization;
using System.Text;

namespace Forge.Api.Capabilities.Discovery;

/// <summary>
/// Phase 4 Phase-F — Pure function that turns a discovery answer set into a
/// preset recommendation, confidence indicator, alternatives, and rationale.
/// Stateless — no DB access, no side effects, deterministic for the same
/// input. The handler that wraps this engine handles the per-install
/// capability-delta computation (because that needs the current snapshot).
///
/// Algorithm (per 4C §Recommendation algorithm Steps 1-7):
///   1. Compute base candidate from branch + headcount + mode + sites.
///   2. Apply regulation override (Q-O4 = yes OR Q-V1 strong signal → PRESET-05).
///   3. Apply override-question shifts (Q-V2 manual checkbox → CUSTOM).
///   4. Compute confidence indicator.
///   5. Build rationale paragraph.
///   6. Surface alternatives if confidence below threshold.
///
/// 4C decision #10 (50-100 + 2 sites → PRESET-06) is enforced via branch
/// routing rule 3: sites &gt;= 2 routes to Branch C regardless of headcount.
/// </summary>
public static class DiscoveryRecommendationEngine
{
    /// <summary>
    /// Confidence threshold for surfacing alternatives. Below this value, the
    /// recommendation includes 1-2 alternatives with one-line distinguishing
    /// rationales (per 4C decision #2, 4F Phase-F decision D3).
    /// </summary>
    public const double AlternativesThreshold = 0.7;

    /// <summary>
    /// Compute a recommendation for the given answer set. Free-text answers
    /// (Q-O2 walkthrough, Q-O6 audit, Q-V1 worst-case, Q-V2 unusual) are
    /// captured into the rationale verbatim but NOT parsed for routing
    /// (per 4C decision #1, 4F Phase-F decision D5).
    /// </summary>
    public static DiscoveryRecommendation Recommend(DiscoveryAnswerSet answers)
    {
        var factors = new List<DiscoveryRecommendationFactor>();

        // ── Step 0: Q-X1 / Q-V2 manual checkbox → PRESET-CUSTOM ─────────
        // (Q-V2 free-text contains "manual" or starts with "yes" — but per
        // 4C decision #1 we don't parse free-text. The wizard surfaces the
        // exit ramp explicitly via Q-X1; if the user picked it, force CUSTOM.)
        var qx1 = answers.Get("Q-X1");
        if (string.Equals(qx1, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return new DiscoveryRecommendation(
                PresetId: "PRESET-CUSTOM",
                Confidence: 1.0,
                ConfidenceLabel: "high",
                Rationale: "You chose to skip discovery and configure capabilities manually. Custom is the empty starting point — toggle each capability you want.",
                Factors: [new DiscoveryRecommendationFactor("Q-X1", "Skip discovery requested")],
                Alternatives: []);
        }

        // ── Step 0.5: Pro Services rollout D4 — Q-S1 short-circuit ──────
        // Top-of-funnel products / services / both split. Services and Hybrid
        // route directly to PRESET-08 / PRESET-09 without traversing the
        // manufacturing-flavored 22-question tree. Products (or unanswered)
        // falls through to existing logic for backward compatibility.
        var businessType = answers.BusinessType;
        if (businessType == "services")
        {
            return new DiscoveryRecommendation(
                PresetId: "PRESET-08",
                Confidence: 1.0,
                ConfidenceLabel: "high",
                Rationale: "You sell time / professional services. Pro Services is the right starting point: " +
                           "task-based work organization (Epic / Project / Story / Bug / Spike), billable hours, " +
                           "retainers, and deliverable tracking — without the manufacturing capabilities a " +
                           "service shop never uses. You can review the capability changes and the terminology " +
                           "bundle before applying anything.",
                Factors: [new DiscoveryRecommendationFactor("Q-S1", "Services-only business → Pro Services")],
                Alternatives:
                [
                    new DiscoveryAlternative(
                        PresetId: "PRESET-09",
                        PresetName: "Hybrid (Make + Service)",
                        DistinguishingRationale: "If you also make or resell physical products, Hybrid carries both stereotypes."),
                    new DiscoveryAlternative(
                        PresetId: "PRESET-CUSTOM",
                        PresetName: "Custom",
                        DistinguishingRationale: "Skip the preset and configure each capability directly."),
                ]);
        }
        if (businessType == "both")
        {
            return new DiscoveryRecommendation(
                PresetId: "PRESET-09",
                Confidence: 1.0,
                ConfidenceLabel: "high",
                Rationale: "You sell both physical products and professional services. Hybrid is the right starting point: " +
                           "carries the full manufacturing stack AND the Pro Services overlay (agile task types — " +
                           "Epic / Project / Story / Bug / Spike — billable hours, deliverables) in one install. " +
                           "Terminology renames Job → Task, Customer → Client, Work Center → Consultant universally.",
                Factors: [new DiscoveryRecommendationFactor("Q-S1", "Make-and-service business → Hybrid")],
                Alternatives:
                [
                    new DiscoveryAlternative(
                        PresetId: "PRESET-08",
                        PresetName: "Pro Services",
                        DistinguishingRationale: "If the make-side is small enough to not warrant the mfg stack, Pro Services alone is lighter."),
                    new DiscoveryAlternative(
                        PresetId: "PRESET-04",
                        PresetName: "Production Manufacturer",
                        DistinguishingRationale: "If the services arm is small enough to skip, Production Manufacturer alone is lighter."),
                ]);
        }

        // ── Step 0.6: Q-O3 services-only → PRESET-08 ────────────────────
        // Q-O3 became multi-choice (services / make / resell) so businesses
        // can check whatever combinations actually apply. Services ALONE
        // (no make, no resell) is the only Q-O3 signal worth a preset
        // reorientation here — it means the user effectively changed
        // their mind at Q-O3 about what their business actually does,
        // overriding the Q-S1=products answer that brought them here.
        //
        // Services checked ALONGSIDE make or resell is just "we sell some
        // services as line items" — falls through to the manufacturing /
        // distribution flow. The existing capability stack handles
        // services-as-line-item via service_uom + time_billable_status
        // reference data without needing a Hybrid preset.
        if (answers.ServicesOnly)
        {
            return new DiscoveryRecommendation(
                PresetId: "PRESET-08",
                Confidence: 1.0,
                ConfidenceLabel: "high",
                Rationale: "You checked Services only at Q-O3 — your business is professional services even " +
                           "though Q-S1 said Products. Q-O3 is the finer-grained signal so we honor it: " +
                           "Pro Services is the right starting point. Task-based work organization (Epic / " +
                           "Project / Story / Bug / Spike), billable hours, retainers, and deliverable tracking " +
                           "— without the manufacturing capabilities a service shop never uses.",
                Factors: [new DiscoveryRecommendationFactor("Q-O3", "Services only (no make/resell) → Pro Services")],
                Alternatives:
                [
                    new DiscoveryAlternative(
                        PresetId: "PRESET-09",
                        PresetName: "Hybrid (Make + Service)",
                        DistinguishingRationale: "If you ALSO make or resell physical products, go back to Q-O3 and check those boxes too — that routes you through the manufacturing flow with services as line items."),
                    new DiscoveryAlternative(
                        PresetId: "PRESET-CUSTOM",
                        PresetName: "Custom",
                        DistinguishingRationale: "Skip the preset and configure each capability directly."),
                ]);
        }

        // ── Step 1: Compute base candidate ──────────────────────────────
        var headcount = answers.HeadcountBucket;
        var mode = answers.Mode;
        var sites = answers.Sites;
        factors.Add(new DiscoveryRecommendationFactor("Q-O1", $"Headcount bucket: {headcount}"));
        factors.Add(new DiscoveryRecommendationFactor("Q-O3", $"Mode: {mode}"));
        factors.Add(new DiscoveryRecommendationFactor("Q-O5", $"Sites: {sites}"));

        var branch = RouteBranch(headcount, sites);
        var baseCandidate = ChooseBaseCandidate(branch, headcount, mode, sites, answers, factors);

        // ── Step 2: Regulation override ─────────────────────────────────
        var regulationOverride = false;
        if (answers.Regulated)
        {
            regulationOverride = true;
            factors.Add(new DiscoveryRecommendationFactor("Q-O4", "Regulated industry — overrides size-based placement"));
        }

        // Soft override: count discrete regulation signals from Q-V1 + Q-D1.
        // Per 4C §Q-V1 §Capability impact: "If two or more of the above signals
        // fire → recommend PRESET-05".
        var softSignalCount = CountSoftRegulationSignals(answers, factors);
        if (!regulationOverride && softSignalCount >= 2)
        {
            regulationOverride = true;
            factors.Add(new DiscoveryRecommendationFactor("Q-V1",
                $"Audit / customer-pressure signals ({softSignalCount}) suggest regulated placement"));
        }

        var candidate = regulationOverride ? "PRESET-05" : baseCandidate;

        // ── Step 3: Free-text capture for rationale (verbatim, not parsed) ──
        var walkthrough = answers.Get("Q-O2");
        var auditProbe = answers.Get("Q-O6");
        var worstCase = answers.Get("Q-V1");
        var unusual = answers.Get("Q-V2");

        // ── Step 4: Confidence ──────────────────────────────────────────
        var (confidenceValue, confidenceLabel) = ComputeConfidence(
            candidate, baseCandidate, regulationOverride, answers, branch, factors);

        // ── Step 5: Rationale ──────────────────────────────────────────
        var preset = PresetCatalog.FindById(candidate)
            ?? throw new InvalidOperationException($"Recommendation produced unknown preset {candidate}");
        var rationale = BuildRationale(preset, regulationOverride, walkthrough,
            auditProbe, worstCase, unusual, factors);

        // ── Step 6: Alternatives ───────────────────────────────────────
        var alternatives = confidenceValue < AlternativesThreshold
            ? FindAlternatives(candidate, baseCandidate, regulationOverride, answers, branch)
            : (IReadOnlyList<DiscoveryAlternative>)[];

        return new DiscoveryRecommendation(
            PresetId: candidate,
            Confidence: confidenceValue,
            ConfidenceLabel: confidenceLabel,
            Rationale: rationale,
            Factors: factors,
            Alternatives: alternatives);
    }

    /// <summary>
    /// 4C §Branch routing rules. Rule 3 (multi-site at any mid+ headcount) wins
    /// over rule 2 — per 4C decision #4 multi-site = yes always routes to
    /// Branch C. Per 4F Phase-F decision D8 / 4C decision #10, 50-100 + 2 sites
    /// resolves to Branch C and PRESET-06.
    /// </summary>
    private static string RouteBranch(string headcount, string sites)
    {
        // 4C decision #4: multi-site = yes → always Branch C
        if (sites is "dual" or "multi" && headcount is not "small")
        {
            return "C";
        }

        return headcount switch
        {
            "small" or "small-mid" => "A",
            "mid" => "B",
            "large" => "C",
            _ => "B", // fallback per 4C §Branch routing rules step 5
        };
    }

    private static string ChooseBaseCandidate(
        string branch, string headcount, string mode, string sites,
        DiscoveryAnswerSet answers, List<DiscoveryRecommendationFactor> factors)
    {
        // Distribution overrides production presets within any size branch
        if (mode == "distribution")
        {
            factors.Add(new DiscoveryRecommendationFactor("Q-O3", "Resell-only mode → Distribution preset"));
            return "PRESET-03";
        }

        return branch switch
        {
            "A" => ChooseBranchAPreset(headcount, mode, answers, factors),
            "B" => ChooseBranchBPreset(answers, factors),
            "C" => ChooseBranchCPreset(headcount, sites, answers, factors),
            _ => "PRESET-02",
        };
    }

    private static string ChooseBranchAPreset(
        string headcount, string mode,
        DiscoveryAnswerSet answers, List<DiscoveryRecommendationFactor> factors)
    {
        // 4C §Step 1 — Branch A:
        //   Two-Person if headcount <= 3 (i.e. "small") AND Q-A2 = same-person
        //   else Growing Job Shop
        var qa2 = answers.Get("Q-A2");
        if (headcount == "small" && qa2 == "same-person")
        {
            factors.Add(new DiscoveryRecommendationFactor("Q-A2", "Same-person operations → Two-Person Shop"));
            return "PRESET-01";
        }

        if (qa2 == "dedicated")
        {
            factors.Add(new DiscoveryRecommendationFactor("Q-A2",
                "Dedicated production lead — consider Production Manufacturer at boundary"));
        }

        return "PRESET-02";
    }

    private static string ChooseBranchBPreset(
        DiscoveryAnswerSet answers, List<DiscoveryRecommendationFactor> factors)
    {
        // 4C §Step 1 — Branch B + production:
        //   PRESET-04 if Q-B1 = formal OR Q-B2 in {formal-ncr, capa-loop}
        //   else PRESET-02 (lower-edge mid)
        var qb1 = answers.Get("Q-B1");
        var qb2 = answers.Get("Q-B2");
        if (qb1 == "formal" || qb2 == "formal-ncr" || qb2 == "capa-loop")
        {
            if (qb1 == "formal")
                factors.Add(new DiscoveryRecommendationFactor("Q-B1", "Formal variance review → Production Manufacturer"));
            if (qb2 == "formal-ncr" || qb2 == "capa-loop")
                factors.Add(new DiscoveryRecommendationFactor("Q-B2", "Formal inspection + NCR/CAPA → Production Manufacturer"));
            return "PRESET-04";
        }
        return "PRESET-02";
    }

    private static string ChooseBranchCPreset(
        string headcount, string sites,
        DiscoveryAnswerSet answers, List<DiscoveryRecommendationFactor> factors)
    {
        // 4C §Step 1 — Branch C:
        //   PRESET-06 if sites in {dual, multi} AND Q-C1 in {daily, weekly}
        //   PRESET-07 if headcount >= 200 AND (Q-C2 = cto-eto OR Q-C3 = yes OR Q-C4 = yes)
        //   PRESET-04 default within branch
        var qc1 = answers.Get("Q-C1");
        var qc2 = answers.Get("Q-C2");
        var qc3 = answers.Get("Q-C3");
        var qc4 = answers.Get("Q-C4");

        // Multi-site signal — 4C decision #10 ensures this wins at 50-100 + 2 sites
        if (sites is "dual" or "multi")
        {
            if (qc1 is "daily" or "weekly")
            {
                factors.Add(new DiscoveryRecommendationFactor("Q-C1", "Frequent inter-site transfers → Multi-Site"));
                return "PRESET-06";
            }

            // Multi-site without strong transfer cadence — still recommend
            // Multi-Site (per 4C decision #10 the 2-site marker dominates)
            factors.Add(new DiscoveryRecommendationFactor("Q-O5", "Multi-site presence → Multi-Site Operation"));
            return "PRESET-06";
        }

        // Single-site large — Enterprise vs Production based on signals
        if (headcount == "large" && (qc2 == "cto-eto"
            || string.Equals(qc3, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(qc4, "yes", StringComparison.OrdinalIgnoreCase)))
        {
            if (qc2 == "cto-eto")
                factors.Add(new DiscoveryRecommendationFactor("Q-C2", "Configure-to-order → Enterprise"));
            if (string.Equals(qc3, "yes", StringComparison.OrdinalIgnoreCase))
                factors.Add(new DiscoveryRecommendationFactor("Q-C3", "EDI integration → Enterprise"));
            if (string.Equals(qc4, "yes", StringComparison.OrdinalIgnoreCase))
                factors.Add(new DiscoveryRecommendationFactor("Q-C4", "Multi-currency → Enterprise"));
            return "PRESET-07";
        }

        return "PRESET-04";
    }

    /// <summary>
    /// Per 4C §Q-V1 §Capability impact. Counts soft regulation signals from
    /// Q-V1 (worst-case) and Q-D1 (lot/serial). 2+ signals → regulation
    /// override even when Q-O4 = no.
    /// </summary>
    private static int CountSoftRegulationSignals(
        DiscoveryAnswerSet answers, List<DiscoveryRecommendationFactor> factors)
    {
        var count = 0;

        // Q-D1 became multi-choice (lots / serials checkboxes). Any
        // non-empty selection is a traceability signal — including legacy
        // single-value answers ("lots" / "serials" / "both") for sessions
        // captured before the migration. Empty or legacy "neither" means
        // no tracking.
        var qd1 = (answers.Get("Q-D1") ?? string.Empty).Trim();
        var tracks = qd1.Length > 0
            && !string.Equals(qd1, "neither", StringComparison.OrdinalIgnoreCase);
        if (tracks)
        {
            count++;
            factors.Add(new DiscoveryRecommendationFactor("Q-D1",
                $"Lot/serial tracking ({qd1}) — traceability signal"));
        }

        // Q-V1 free text: 4C decision #1 says don't parse, BUT a non-empty
        // worst-case answer that mentions regulator-grade pressure is itself
        // a soft signal. Per 4F Phase-F decision D5 we don't NLP this; we
        // simply note when a non-empty answer exists for the rationale.
        var qv1 = answers.Get("Q-V1");
        if (!string.IsNullOrWhiteSpace(qv1))
        {
            // Length-based heuristic — a substantive answer (>= 40 chars)
            // counts as a soft signal. Short answers ("nothing", "no") do not.
            if (qv1.Trim().Length >= 40)
            {
                count++;
                factors.Add(new DiscoveryRecommendationFactor("Q-V1",
                    "Substantial worst-case audit description provided"));
            }
        }

        return count;
    }

    private static (double Value, string Label) ComputeConfidence(
        string candidate, string baseCandidate, bool regulationOverride,
        DiscoveryAnswerSet answers, string branch,
        List<DiscoveryRecommendationFactor> factors)
    {
        // High: branch answers consistent with base; no override fired
        // Medium: one branch answer points elsewhere; or regulated but Q-D1=neither
        // Low: two+ contradictory signals; Q-V2 unusual described; boundary headcount
        var contradictions = 0;

        // Boundary headcount? "11-25" vs "26-50" is the size-mid boundary
        var qo1 = answers.Get("Q-O1");
        if (qo1 == "11-25" || qo1 == "26-50")
        {
            contradictions++;
        }

        // Regulated + Q-D1 = neither → contradiction (4C §Q-D1 interp)
        if (regulationOverride && answers.Get("Q-D1") == "neither")
        {
            contradictions++;
            factors.Add(new DiscoveryRecommendationFactor("Q-D1",
                "Regulated but no lot/serial tracking — confidence flagged"));
        }

        // Q-V2 free text non-empty → unusual described
        var qv2 = answers.Get("Q-V2");
        if (!string.IsNullOrWhiteSpace(qv2) && qv2.Trim().Length >= 40)
        {
            contradictions++;
        }

        // Branch B "split" answers (one branch question pulls each way)
        if (branch == "B")
        {
            var qb1 = answers.Get("Q-B1");
            var qb2 = answers.Get("Q-B2");
            var hasFormalSignal = qb1 == "formal" || qb2 is "formal-ncr" or "capa-loop";
            var hasInformalSignal = qb1 is "no" or "informal" || qb2 is "visual" or "informal";
            if (hasFormalSignal && hasInformalSignal) contradictions++;
        }

        return contradictions switch
        {
            0 => (1.0, "high"),
            1 => (0.6, "medium"),
            _ => (0.4, "low"),
        };
    }

    private static string BuildRationale(
        PresetDefinition preset, bool regulationOverride,
        string? walkthrough, string? auditProbe,
        string? worstCase, string? unusual,
        IReadOnlyList<DiscoveryRecommendationFactor> factors)
    {
        var sb = new StringBuilder();

        // Free-text walk-through quoted at the top (per 4F Phase-F decision D5).
        if (!string.IsNullOrWhiteSpace(walkthrough))
        {
            sb.Append("You described your business as: \"")
              .Append(walkthrough.Trim())
              .Append("\". ");
        }

        sb.Append("Based on your answers we recommend ")
          .Append(preset.Name)
          .Append(" — ")
          .Append(preset.ShortDescription);

        if (regulationOverride)
        {
            sb.Append(" Regulation overrides the default size-based placement: small shops in regulated industries get the same QC stack as larger ones.");
        }

        if (!string.IsNullOrWhiteSpace(auditProbe))
        {
            sb.Append(" Audit / customer pressure noted: \"")
              .Append(auditProbe.Trim())
              .Append("\".");
        }
        if (!string.IsNullOrWhiteSpace(worstCase))
        {
            sb.Append(" Worst-case scenario you flagged: \"")
              .Append(worstCase.Trim())
              .Append("\".");
        }
        if (!string.IsNullOrWhiteSpace(unusual))
        {
            sb.Append(" Unusual aspects you mentioned: \"")
              .Append(unusual.Trim())
              .Append("\".");
        }

        sb.Append(" You can review the capability changes before applying anything.");

        return sb.ToString();
    }

    private static IReadOnlyList<DiscoveryAlternative> FindAlternatives(
        string candidate, string baseCandidate, bool regulationOverride,
        DiscoveryAnswerSet answers, string branch)
    {
        var alternatives = new List<DiscoveryAlternative>();

        // If regulation override fired but base candidate differs, surface the
        // base candidate as an alternative — "your size suggests X, but..."
        if (regulationOverride && baseCandidate != candidate)
        {
            var altPreset = PresetCatalog.FindById(baseCandidate);
            if (altPreset is not null)
            {
                alternatives.Add(new DiscoveryAlternative(
                    PresetId: baseCandidate,
                    PresetName: altPreset.Name,
                    DistinguishingRationale: $"Closer to your size if regulation is lighter than indicated."));
            }
        }

        // Boundary alternatives — Branch B 02 vs 04
        if (branch == "B" && !regulationOverride)
        {
            var alt = candidate == "PRESET-04" ? "PRESET-02" : "PRESET-04";
            var altPreset = PresetCatalog.FindById(alt);
            if (altPreset is not null)
            {
                alternatives.Add(new DiscoveryAlternative(
                    PresetId: alt,
                    PresetName: altPreset.Name,
                    DistinguishingRationale: alt == "PRESET-02"
                        ? "Lighter — for shops without formal variance / inspection workflows yet."
                        : "Heavier — adds variance, NCR, CAPA, approvals, OEE."));
            }
        }

        // Mid + multi-site boundary — PRESET-04 vs PRESET-06
        if (candidate == "PRESET-06" && answers.Get("Q-C1") == "rarely")
        {
            var altPreset = PresetCatalog.FindById("PRESET-04");
            if (altPreset is not null)
            {
                alternatives.Add(new DiscoveryAlternative(
                    PresetId: "PRESET-04",
                    PresetName: altPreset.Name,
                    DistinguishingRationale: "If transfers between sites are rare, two independent single-site setups may fit better."));
            }
        }

        // Custom always available as a final-resort alternative
        if (alternatives.Count < 2)
        {
            alternatives.Add(new DiscoveryAlternative(
                PresetId: "PRESET-CUSTOM",
                PresetName: "Custom",
                DistinguishingRationale: "Skip the preset and configure each capability directly."));
        }

        return alternatives.Take(2).ToList();
    }

    /// <summary>
    /// Phase 4 Phase-F — Compute the capability-state delta between the
    /// install's current snapshot and the chosen preset's target. Used by
    /// the preview endpoint and the apply orchestration.
    /// </summary>
    public static IReadOnlyList<CapabilityDelta> ComputeDeltas(
        string presetId,
        IReadOnlyDictionary<string, bool> currentState)
    {
        var preset = PresetCatalog.FindById(presetId)
            ?? throw new ArgumentException($"Unknown preset id: {presetId}", nameof(presetId));

        // Custom: per 4B Open Question 5 / 4F Phase-F decision, inherits the
        // 41 catalog defaults at apply-time. The "target" is the catalog
        // default-on set.
        var targetSet = preset.IsCustom
            ? new HashSet<string>(
                CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code),
                StringComparer.Ordinal)
            : new HashSet<string>(preset.EnabledCapabilities, StringComparer.Ordinal);

        var deltas = new List<CapabilityDelta>();

        // Walk every known capability and compare current vs target.
        foreach (var def in CapabilityCatalog.All)
        {
            var currently = currentState.TryGetValue(def.Code, out var c) && c;
            var willBe = targetSet.Contains(def.Code);
            if (currently != willBe)
            {
                deltas.Add(new CapabilityDelta(def.Code, def.Name, currently, willBe));
            }
        }

        return deltas
            .OrderBy(d => d.Code, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>
/// One row in the capability-delta preview: code, name, current state, and
/// the state it will move to if the preset is applied.
/// </summary>
public record CapabilityDelta(string Code, string Name, bool CurrentlyEnabled, bool WillBeEnabled);
