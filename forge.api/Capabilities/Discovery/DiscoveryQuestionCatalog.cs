namespace Forge.Api.Capabilities.Discovery;

/// <summary>
/// Phase 4 Phase-F — Authoritative static catalog of every discovery
/// question. Mirrors the Phase 4C discovery flow markdown
/// (e:/dev/forge/phase-4-output/4C-discovery-flow/discovery-flow.md).
///
/// 22 self-serve questions:
///   • 6 opening (Q-O1..Q-O6)
///   • 4 Branch A (small)
///   • 4 Branch B (mid)
///   • 4 Branch C (large)
///   • 2 override (Q-V1, Q-V2)
///   • 6 diagnostic (Q-D1..Q-D6)
///   • 1 exit ramp (Q-X1)
///
/// Plus consultant-mode deepdive questions (per 4C decision #6: 6-8 per
/// branch) marked with <see cref="DiscoveryCategory.ConsultantDeepdive"/>;
/// the wizard surfaces them only when consultant mode is on.
///
/// IMPORTANT: question IDs (e.g. "Q-O1") are stable. Wizard answers persist
/// keyed by ID; renaming would break audit trails. Use additive shape changes
/// only.
/// </summary>
public static class DiscoveryQuestionCatalog
{
    // ── Top-of-funnel (Q-S1) ───────────────────────────────────────────────
    // Pro Services rollout D4: split products / services / both at the very
    // top of the wizard. "services" short-circuits to PRESET-08 (Pro Services);
    // "both" short-circuits to PRESET-09 (Hybrid); "products" falls through to
    // the existing manufacturing-flavored 22 questions. Unanswered = backward
    // compatible (engine treats as "products" and falls through).

    private static readonly DiscoveryQuestion QS1 = new(
        Id: "Q-S1",
        Stage: DiscoveryStage.Opening,
        Category: DiscoveryCategory.Opening,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "What does your business primarily sell — physical products you make or resell, time and services your team delivers, or both?",
        WhyAsking: "This is the biggest fork in the road. A consulting firm or agency needs a fundamentally different setup than a manufacturer or distributor. If you sell time you should never be asked about BOMs, routings, or shop floor; if you do both, you need the union.",
        Choices:
        [
            new("products", "We sell physical products"),
            new("services", "We sell time / professional services"),
            new("both",     "We do both"),
        ]);

    // ── Opening (Q-O1 .. Q-O6) ────────────────────────────────────────────

    private static readonly DiscoveryQuestion QO1 = new(
        Id: "Q-O1",
        Stage: DiscoveryStage.Opening,
        Category: DiscoveryCategory.Opening,
        Type: DiscoveryQuestionType.Bucketed,
        Text: "Roughly how many people work in your business — including yourself, anyone in the office, and anyone on the shop floor or in the warehouse?",
        WhyAsking: "Headcount is the single best predictor of organisational complexity — it's where we start, but it isn't destiny. A small regulated shop is still regulated.",
        Choices:
        [
            new("1-2", "1–2 people"),
            new("3-10", "3–10 people"),
            new("11-25", "11–25 people"),
            new("26-50", "26–50 people"),
            new("51-200", "51–200 people"),
            new("200+", "200+ people"),
        ]);

    private static readonly DiscoveryQuestion QO2 = new(
        Id: "Q-O2",
        Stage: DiscoveryStage.Opening,
        Category: DiscoveryCategory.Opening,
        Type: DiscoveryQuestionType.FreeText,
        Text: "Walk me through what happens from when a customer asks for a price to when you get paid. Who touches it along the way?",
        WhyAsking: "This open-ended prompt surfaces role separation, workflow shape, and the rhythm of the business in a way no checkbox can.");

    private static readonly DiscoveryQuestion QO3 = new(
        Id: "Q-O3",
        Stage: DiscoveryStage.Opening,
        Category: DiscoveryCategory.Opening,
        Type: DiscoveryQuestionType.MultiChoice,
        Text: "What does your business actually do? Check every line that applies — the more accurate this is, the better the preset.",
        WhyAsking: "Q-S1 is the top-of-funnel fast-path; this is the finer reality check. Many businesses are a " +
                   "combination — a manufacturer that also resells partner products, an engineering firm that ships " +
                   "physical parts, a distributor with a consulting arm. Checking Services alongside one of the " +
                   "product options routes you to the Hybrid preset (Pro Services overlay + manufacturing stack). " +
                   "Services-only routes to Pro Services even if Q-S1 said Products.",
        Choices:
        [
            new("services", "We deliver professional services — consulting, agency work, engineering hours; any business model where we're selling our time and expertise"),
            new("make", "We make physical products — we build, assemble, fabricate, or manufacture from raw materials or components"),
            new("resell", "We resell physical products we buy from suppliers — distribution, wholesale, retail, drop-ship"),
        ]);

    private static readonly DiscoveryQuestion QO4 = new(
        Id: "Q-O4",
        Stage: DiscoveryStage.Opening,
        Category: DiscoveryCategory.Opening,
        Type: DiscoveryQuestionType.MultiChoice,
        Text: "Are you in a regulated industry, or do you carry quality certifications? Check every box that applies — many businesses serve multiple regulated markets (e.g. medical AND aerospace, or food AND pharma).",
        WhyAsking: "Regulation is orthogonal to size — a small shop with FDA QSR or AS9100 obligations has the same compliance overhead as a large one. ANY certification selected here recommends the Regulated Manufacturer preset regardless of headcount. Capturing all applicable certs (not just the first one) lets the install pre-populate compliance documents and audit trails for each.",
        Choices:
        [
            new("no", "No, none of these apply"),
            new("medical", "Medical devices (ISO 13485, FDA QSR)"),
            new("aerospace", "Aerospace (AS9100)"),
            new("automotive", "Automotive (IATF 16949)"),
            new("food", "Food (FSMA, GMP)"),
            new("pharma", "Pharma (cGMP)"),
            new("other", "Something else (specify in the audit-walkthrough free-text below)"),
        ]);

    private static readonly DiscoveryQuestion QO5 = new(
        Id: "Q-O5",
        Stage: DiscoveryStage.Opening,
        Category: DiscoveryCategory.Opening,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "How many physical locations do you operate from — counting production sites, warehouses, and any combination?",
        WhyAsking: "Multi-site is a load-bearing distinction at mid-and-large headcount. Two or more sites with regular inter-site transfers lands on Multi-Site Operation.",
        Choices:
        [
            new("1", "1 location"),
            new("2", "2 locations"),
            new("3+", "3 or more locations"),
        ]);

    private static readonly DiscoveryQuestion QO6 = new(
        Id: "Q-O6",
        Stage: DiscoveryStage.Opening,
        Category: DiscoveryCategory.Opening,
        Type: DiscoveryQuestionType.FreeText,
        Text: "If an auditor walked in tomorrow, or your biggest customer asked you to prove something about a product you shipped — what would they ask, and how long would it take you to answer?",
        WhyAsking: "This catches small shops with heavy compliance / traceability pressure that the size-based defaults would miss.");

    // ── Branch A (small, 1-25) ────────────────────────────────────────────

    private static readonly DiscoveryQuestion QA1 = new(
        Id: "Q-A1",
        Stage: DiscoveryStage.BranchA,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Do you currently use accounting software like QuickBooks, Xero, or something similar — or do invoices and payments live only in your shop system or spreadsheets?",
        WhyAsking: "This determines the accounting mode (built-in vs external). Operators already on QuickBooks should keep their books there.",
        Branch: "A",
        Choices:
        [
            new("none", "No software — spreadsheets / paper / shop system"),
            new("quickbooks", "QuickBooks Online or Desktop"),
            new("xero", "Xero"),
            new("other", "Other accounting software"),
        ]);

    private static readonly DiscoveryQuestion QA2 = new(
        Id: "Q-A2",
        Stage: DiscoveryStage.BranchA,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Is anyone in your business full-time on production scheduling or shop-floor management — or does the same person who quotes also schedule and run the shop?",
        WhyAsking: "Splits the Two-Person Shop preset (one person does everything) from Growing Job Shop (clear front-office vs floor split).",
        Branch: "A",
        Choices:
        [
            new("same-person", "Same person does everything"),
            new("split-roles", "Different people in office and shop, no dedicated scheduler"),
            new("dedicated", "We have a dedicated production lead or scheduler"),
        ]);

    private static readonly DiscoveryQuestion QA3 = new(
        Id: "Q-A3",
        Stage: DiscoveryStage.BranchA,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "When you make something, does it go through one machine or step from start to finish, or does it move through several different machines or stations?",
        WhyAsking: "Single-step shops should disable multi-op routing surfaces to avoid noise. This question removes that noise without forcing the user to know the term \"routing.\"",
        Branch: "A",
        Choices:
        [
            new("single-step", "One step / one machine"),
            new("two-three", "Two or three steps"),
            new("multi-step", "Several machines or stations, with handoffs"),
        ]);

    private static readonly DiscoveryQuestion QA4 = new(
        Id: "Q-A4",
        Stage: DiscoveryStage.BranchA,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Are most of your sales orders shipped from your warehouse, or sometimes shipped directly from your supplier to your customer without going through you?",
        WhyAsking: "Catches drop-ship and back-to-back patterns. Asked only when you said you resell or do both.",
        Branch: "A",
        Choices:
        [
            new("warehouse", "All from our warehouse"),
            new("some-dropship", "Some drop-ship from our supplier"),
            new("mostly-dropship", "Mostly drop-ship"),
        ]);

    // ── Branch B (mid, 25-200 single-site) ───────────────────────────────

    private static readonly DiscoveryQuestion QB1 = new(
        Id: "Q-B1",
        Stage: DiscoveryStage.BranchB,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "After a job ships and is invoiced, do you compare what it actually cost you to make against what you quoted — and act on the difference?",
        WhyAsking: "Splits Growing Job Shop from Production Manufacturer. Job-cost variance review signals the shop has the data discipline for formal control surfaces.",
        Branch: "B",
        Choices:
        [
            new("no", "We don't"),
            new("informal", "We look but don't act"),
            new("formal", "Yes, formally — variance is reviewed and acted on"),
        ]);

    private static readonly DiscoveryQuestion QB2 = new(
        Id: "Q-B2",
        Stage: DiscoveryStage.BranchB,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Beyond a final visual look, do you do incoming, in-process, or first-article inspection — and do you formally write up nonconformances when something is wrong?",
        WhyAsking: "The ISO 9001 baseline question — inspection plus NCR plus CAPA. Shops doing it land on Production Manufacturer at minimum.",
        Branch: "B",
        Choices:
        [
            new("visual", "Just visual"),
            new("informal", "Some inspection, informal"),
            new("formal-ncr", "Formal inspection plans + NCR"),
            new("capa-loop", "Full CAPA loop closing inspection-NCR-corrective-action"),
        ]);

    private static readonly DiscoveryQuestion QB3 = new(
        Id: "Q-B3",
        Stage: DiscoveryStage.BranchB,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.YesNo,
        Text: "For purchase orders above a certain dollar amount, do you have a formal approval step — somebody other than the buyer signs off — or does the buyer just place the order?",
        WhyAsking: "Approval workflow is a strong Production Manufacturer signal. At Growing Job Shop scale, the buyer is often the owner; at PM scale, the buyer is not the owner and approvals matter.",
        Branch: "B");

    private static readonly DiscoveryQuestion QB4 = new(
        Id: "Q-B4",
        Stage: DiscoveryStage.BranchB,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do you send work out to other shops for finishing — heat treat, plating, painting, coating, anodizing — and need to track that round-trip?",
        WhyAsking: "Subcontract is universal in real machine shops at mid-scale. Asking explicitly captures the parameter.",
        Branch: "B");

    // ── Branch C (large, 200+ or 2+ sites) ────────────────────────────────

    private static readonly DiscoveryQuestion QC1 = new(
        Id: "Q-C1",
        Stage: DiscoveryStage.BranchC,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "How often do you move inventory between your locations — every day, every week, monthly, or rarely?",
        WhyAsking: "Splits Multi-Site (frequent inter-site transfers) from a single-site Production Manufacturer.",
        Branch: "C",
        Choices:
        [
            new("daily", "Daily"),
            new("weekly", "Weekly"),
            new("monthly", "Monthly"),
            new("rarely", "Rarely or never"),
        ]);

    private static readonly DiscoveryQuestion QC2 = new(
        Id: "Q-C2",
        Stage: DiscoveryStage.BranchC,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Do your customers buy configurable products — choose options, sizes, materials, with the price calculated from those choices — or are your products mostly fixed-spec?",
        WhyAsking: "CPQ is the defining mid-market vs. enterprise question. PRESET-07 has it on by default; PRESET-06 does not.",
        Branch: "C",
        Choices:
        [
            new("fixed", "Fixed-spec products"),
            new("some-config", "Some configuration"),
            new("cto-eto", "Configure-to-order or engineer-to-order is core"),
        ]);

    private static readonly DiscoveryQuestion QC3 = new(
        Id: "Q-C3",
        Stage: DiscoveryStage.BranchC,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do any of your major customers send purchase orders or expect shipping notices through EDI — those structured 850, 855, 856, 810 documents?",
        WhyAsking: "EDI is on by default in PRESET-06 and PRESET-07. Asking explicitly catches the cases where it isn't and should be turned off.",
        Branch: "C");

    private static readonly DiscoveryQuestion QC4 = new(
        Id: "Q-C4",
        Stage: DiscoveryStage.BranchC,
        Category: DiscoveryCategory.BranchSpecific,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do you operate in more than one currency — selling to international customers, buying from international suppliers, or running a site in another country?",
        WhyAsking: "Multi-currency is a PRESET-07 default. Single-currency multi-site stays with PRESET-06.",
        Branch: "C");

    // ── Override (Q-V1, Q-V2) ─────────────────────────────────────────────

    private static readonly DiscoveryQuestion QV1 = new(
        Id: "Q-V1",
        Stage: DiscoveryStage.Override,
        Category: DiscoveryCategory.Override,
        Type: DiscoveryQuestionType.FreeText,
        Text: "What's the worst thing a regulator or your biggest customer could ask you to prove — about a product, a process, a person's training — and how confident are you that you could prove it today?",
        WhyAsking: "The override probe. Catches lot-trace pressure, gage calibration audits, customer-mandated traceability — situations where the size-based default placement is wrong.");

    private static readonly DiscoveryQuestion QV2 = new(
        Id: "Q-V2",
        Stage: DiscoveryStage.Override,
        Category: DiscoveryCategory.Override,
        Type: DiscoveryQuestionType.FreeText,
        Text: "Is there anything unusual about how your business runs — something none of the standard descriptions would capture — that we should know about?",
        WhyAsking: "Catches the edge cases that warrant Custom configuration. Also surfaces situations where two presets would otherwise stack.");

    // ── Diagnostic (Q-D1 .. Q-D6) ─────────────────────────────────────────
    // Per 4C decision #5: order by capability impact descending; skip
    // diagnostics already answered by branch-specific questions.

    private static readonly DiscoveryQuestion QD1 = new(
        Id: "Q-D1",
        Stage: DiscoveryStage.Diagnostic,
        Category: DiscoveryCategory.Diagnostic,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Do you track parts by lot number, by serial number, both, or neither?",
        WhyAsking: "Disambiguates the Regulated Manufacturer baseline (recall depends on at least one of lot or serial). Production-preset shops without compliance pressure may still want lot or serial for customer demand.",
        Choices:
        [
            new("neither", "Neither"),
            new("lots", "Lot only"),
            new("serials", "Serial only"),
            new("both", "Both"),
        ]);

    private static readonly DiscoveryQuestion QD2 = new(
        Id: "Q-D2",
        Stage: DiscoveryStage.Diagnostic,
        Category: DiscoveryCategory.Diagnostic,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do you handle hazardous materials, chemicals, or anything that needs SDS sheets, special handling, or regulated transportation?",
        WhyAsking: "Catches industrial-supply, paint, chemical, and food-safety scenarios. Drives the hazmat capability.");

    private static readonly DiscoveryQuestion QD3 = new(
        Id: "Q-D3",
        Stage: DiscoveryStage.Diagnostic,
        Category: DiscoveryCategory.Diagnostic,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Do you have a preventive maintenance schedule for any of your equipment, and do you track machine breakdowns when they happen?",
        WhyAsking: "PM and breakdown tracking are PRESET-04+ defaults. Asking lets the small-shop operator opt in if they have valuable equipment.",
        Choices:
        [
            new("breakfix", "We just fix it when it breaks"),
            new("informal-pm", "PM schedule exists informally"),
            new("formal", "Formal PM + breakdown logging"),
            new("iot", "Machine telemetry / IoT data feeds"),
        ]);

    private static readonly DiscoveryQuestion QD4 = new(
        Id: "Q-D4",
        Stage: DiscoveryStage.Diagnostic,
        Category: DiscoveryCategory.Diagnostic,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Do your shop-floor or warehouse workers have email accounts and personal logins, or do they share a kiosk or terminal? And do you run shifts, or is everyone on a single schedule?",
        WhyAsking: "Two follow-ups bundled: kiosk auth need, and whether shifts apply. Both feed capability defaults.",
        Choices:
        [
            new("email-single", "Everyone has email; single schedule"),
            new("email-shifts", "Everyone has email; multiple shifts"),
            new("kiosk-single", "Shared terminal / kiosk; single schedule"),
            new("kiosk-shifts", "Shared terminal / kiosk; multiple shifts"),
        ]);

    private static readonly DiscoveryQuestion QD5 = new(
        Id: "Q-D5",
        Stage: DiscoveryStage.Diagnostic,
        Category: DiscoveryCategory.Diagnostic,
        Type: DiscoveryQuestionType.MultiChoice,
        Text: "Do you have an IT person or supply-chain team who would integrate this system with others — pushing data into BI tools like PowerBI or Tableau, sending events to Slack or Teams, or making outbound API calls?",
        WhyAsking: "Integration capabilities (webhooks, BI export, API keys, chat) are off by default in everything below PRESET-07. Asking once captures all four.",
        Choices:
        [
            new("none", "No IT team / not interested"),
            new("bi", "BI export / dashboarding (PowerBI, Tableau)"),
            new("chat", "Slack / Teams / Discord notifications"),
            new("api", "Outbound webhooks or API integrations"),
        ]);

    private static readonly DiscoveryQuestion QD6 = new(
        Id: "Q-D6",
        Stage: DiscoveryStage.Diagnostic,
        Category: DiscoveryCategory.Diagnostic,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "Do you make the same things over and over — repeat parts you have a quote and a routing for — or is most of what you do custom each time, with new quotes and new processes?",
        WhyAsking: "Splits lean / repetitive customizations from job-shop customizations within Production Manufacturer.",
        Choices:
        [
            new("repeat", "Mostly repeat"),
            new("mix", "Mix of repeat and custom"),
            new("custom", "Mostly custom each time"),
        ]);

    // ── Exit ramp (Q-X1) ──────────────────────────────────────────────────

    private static readonly DiscoveryQuestion QX1 = new(
        Id: "Q-X1",
        Stage: DiscoveryStage.Exit,
        Category: DiscoveryCategory.Exit,
        Type: DiscoveryQuestionType.YesNo,
        Text: "None of these descriptions matches my business, or I'd rather configure capabilities directly. Skip discovery and start from a blank Custom configuration.",
        WhyAsking: "Custom must be reachable without going through the flow. Available throughout the wizard as a banner / link.");

    // ── Consultant deepdive (per 4C decision #6) ───────────────────────────
    // 4-6 per branch; surfaced only when consultant mode is on.

    private static readonly DiscoveryQuestion QA5_DD = new(
        Id: "Q-A5",
        Stage: DiscoveryStage.BranchA,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Have you had a customer ask for a quote and you couldn't reproduce the price you gave them last time?",
        WhyAsking: "Consultant deepdive — catches small-shop pricing-discipline issues that suggest moving to Growing Job Shop.",
        Branch: "A",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QA6_DD = new(
        Id: "Q-A6",
        Stage: DiscoveryStage.BranchA,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do you ever have to refuse a job because you can't tell what's already in the queue?",
        WhyAsking: "Consultant deepdive — visibility-into-WIP signal; reinforces shop-floor capability needs at smaller scale.",
        Branch: "A",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QA7_DD = new(
        Id: "Q-A7",
        Stage: DiscoveryStage.BranchA,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.SingleChoice,
        Text: "When something goes wrong on a part, how do you find the materials it was made from?",
        WhyAsking: "Consultant deepdive — soft regulation signal; small-but-traceability-aware shops should consider Regulated Manufacturer.",
        Branch: "A",
        VisibleInSelfServe: false,
        Choices:
        [
            new("memory", "Memory / asking around"),
            new("paper", "Paper records / job folder"),
            new("system", "System-tracked lots / serials"),
            new("nothing", "We can't find it"),
        ]);

    private static readonly DiscoveryQuestion QA8_DD = new(
        Id: "Q-A8",
        Stage: DiscoveryStage.BranchA,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Have you ever lost a sale because you couldn't promise an on-time delivery date?",
        WhyAsking: "Consultant deepdive — surfaces planning-capability gap at small scale.",
        Branch: "A",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QB5_DD = new(
        Id: "Q-B5",
        Stage: DiscoveryStage.BranchB,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Has your accountant or controller ever asked for a report you couldn't produce from your shop system?",
        WhyAsking: "Consultant deepdive — finance-data-discipline signal; suggests moving toward Production Manufacturer reporting depth.",
        Branch: "B",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QB6_DD = new(
        Id: "Q-B6",
        Stage: DiscoveryStage.BranchB,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do you find yourself running MRP-like calculations in Excel — pegging supply against demand, or chasing shortages?",
        WhyAsking: "Consultant deepdive — strong indicator that CAP-PLAN-MRP should be on the recommendation.",
        Branch: "B",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QB7_DD = new(
        Id: "Q-B7",
        Stage: DiscoveryStage.BranchB,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do you have customers who require a Certificate of Conformance or Certificate of Analysis with each shipment?",
        WhyAsking: "Consultant deepdive — soft regulation signal even for unregulated shops.",
        Branch: "B",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QB8_DD = new(
        Id: "Q-B8",
        Stage: DiscoveryStage.BranchB,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Has a quality issue ever been escalated to a corrective action that took longer than 30 days to resolve?",
        WhyAsking: "Consultant deepdive — CAPA discipline question; reinforces full QC stack recommendation.",
        Branch: "B",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QC5_DD = new(
        Id: "Q-C5",
        Stage: DiscoveryStage.BranchC,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do any of your sites operate under a different fiscal calendar, currency, or tax regime?",
        WhyAsking: "Consultant deepdive — reinforces multi-currency / consolidated-reporting need at Enterprise tier.",
        Branch: "C",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QC6_DD = new(
        Id: "Q-C6",
        Stage: DiscoveryStage.BranchC,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do you run a master production schedule that's reviewed in a formal weekly or monthly meeting?",
        WhyAsking: "Consultant deepdive — MPS adoption signal; surfaces CAP-PLAN-MPS need.",
        Branch: "C",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QC7_DD = new(
        Id: "Q-C7",
        Stage: DiscoveryStage.BranchC,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do you have a formal sales-and-operations planning (S&OP) process?",
        WhyAsking: "Consultant deepdive — reinforces forecast + capacity capability stack.",
        Branch: "C",
        VisibleInSelfServe: false);

    private static readonly DiscoveryQuestion QC8_DD = new(
        Id: "Q-C8",
        Stage: DiscoveryStage.BranchC,
        Category: DiscoveryCategory.ConsultantDeepdive,
        Type: DiscoveryQuestionType.YesNo,
        Text: "Do your major customers run vendor scorecards or expect quarterly business reviews on quality and delivery metrics?",
        WhyAsking: "Consultant deepdive — surfaces the value of OEE + dashboarding capabilities.",
        Branch: "C",
        VisibleInSelfServe: false);

    /// <summary>
    /// All questions, keyed by ID for fast lookup. Order is wizard-rendering
    /// order: opening → branches in stage order → override → diagnostic → exit
    /// → consultant deepdives appended.
    /// </summary>
    public static IReadOnlyList<DiscoveryQuestion> All { get; } = new List<DiscoveryQuestion>
    {
        QS1,  // Pro Services rollout D4 — top-of-funnel products/services/both split.
        QO1, QO2, QO3, QO4, QO5, QO6,
        QA1, QA2, QA3, QA4,
        QB1, QB2, QB3, QB4,
        QC1, QC2, QC3, QC4,
        QV1, QV2,
        QD1, QD2, QD3, QD4, QD5, QD6,
        QX1,
        // Consultant deepdive — surfaced only when mode = consultant.
        QA5_DD, QA6_DD, QA7_DD, QA8_DD,
        QB5_DD, QB6_DD, QB7_DD, QB8_DD,
        QC5_DD, QC6_DD, QC7_DD, QC8_DD,
    };

    /// <summary>
    /// Self-serve catalog count: 28 = 1 top-of-funnel (Q-S1, Pro Services D4) +
    /// 6 opening + 4 Branch A + 4 Branch B + 4 Branch C + 2 override + 6 diagnostic
    /// + 1 exit. A given user typically answers far fewer because only one branch's
    /// questions apply AND Q-S1 = "services" / "both" short-circuits the entire
    /// manufacturing tree. The catalog ships all 28.
    /// </summary>
    public const int SelfServeCount = 28;

    /// <summary>Per-user count after branch routing — what the 4C design calls "22 questions." For Q-S1 = products this is still 22; for services/both, it's effectively 1.</summary>
    public const int PerUserAnsweredCount = 22;

    /// <summary>Consultant deepdive count: 12 across 3 branches (4 per branch).</summary>
    public const int ConsultantDeepdiveCount = 12;

    /// <summary>Look up a question by ID. Returns null on unknown.</summary>
    public static DiscoveryQuestion? FindById(string id) =>
        All.FirstOrDefault(q => string.Equals(q.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Filter the catalog for a given mode. Self-serve sees the 22; consultant
    /// sees the 22 + applicable deepdive (filtered later by branch).
    /// </summary>
    public static IEnumerable<DiscoveryQuestion> ForMode(bool consultantMode) =>
        consultantMode
            ? All
            : All.Where(q => q.VisibleInSelfServe);
}
