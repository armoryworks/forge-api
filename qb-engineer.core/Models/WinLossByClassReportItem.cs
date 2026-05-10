namespace QBEngineer.Core.Models;

/// <summary>
/// Phase 1r / Batch 16 — win/loss by RFQ part-class cohort. The
/// PartClassCode on Lead is a free-text taxonomy ("machining-stainless",
/// "injection-plastic", "sheet-metal") that groups leads by the work
/// they'd produce if won. The cohort lets sales managers see which
/// commodities the shop wins and which it loses, distinct from generic
/// "lead status" reporting.
/// </summary>
public record WinLossByClassReportItem(
    string PartClassCode,
    int Converted,
    int Lost,
    int Active,
    int TotalLeads,
    double WinRatePct);
