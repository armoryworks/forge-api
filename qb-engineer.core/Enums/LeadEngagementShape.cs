namespace QBEngineer.Core.Enums;

/// <summary>
/// Wave 7 — classification axis on Lead that informs sales approach + intake
/// shape. Mirrors the Parts axis-driven onboarding pattern. Default is
/// <see cref="Unknown"/> so existing leads + the "Quick add" New Lead path
/// (skip-the-axis) round-trip cleanly without forcing a value at creation.
///
/// The fork dialog routes to a different intake-form variant per shape —
/// e.g. QuickQuote captures parts/qty/due-date upfront; Strategic captures
/// decision-maker + current-vendor. Specialised fields land in the existing
/// <c>Lead.CustomFieldValues</c> JSONB blob so adding new shapes doesn't
/// require schema work.
/// </summary>
public enum LeadEngagementShape
{
    /// <summary>Quick-add path or pre-Wave-7 lead that wasn't classified.</summary>
    Unknown = 0,

    /// <summary>Inbound RFQ / one-off transactional. Customer has a formed
    /// need (parts list + target due date). Sales motion: quote within 24-48h
    /// or the lead goes cold.</summary>
    QuickQuote = 1,

    /// <summary>Standing relationship / repeat business. Customer has an
    /// existing master agreement or recurring PO pattern. Sales motion:
    /// reference prior job + price book; quick turn.</summary>
    Repeat = 2,

    /// <summary>Strategic account / long sales cycle. Multi-stakeholder,
    /// multi-meeting cadence. Sales motion: decision-maker + champion
    /// mapping; longer follow-up windows by default.</summary>
    Strategic = 3,

    /// <summary>Prototype / R&amp;D / engineering-first. Custom, exploratory,
    /// not a repeat shape. Sales motion: capability fit-check before a
    /// formal quote.</summary>
    Prototype = 4,
}
