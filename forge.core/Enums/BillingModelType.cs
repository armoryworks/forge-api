namespace Forge.Core.Enums;

/// <summary>
/// Pro Services billing model on a Job (engagement). Per the G-17 spike,
/// Engagement = Job on the Engagement track type; this field gates how
/// time + expenses roll up to invoice.
///
/// Stored as int by EF; serialized as the C# name string by
/// JsonStringEnumConverter at the API boundary.
/// </summary>
public enum BillingModelType
{
    /// <summary>Time-and-materials: invoice = billable hours × rate + expenses.</summary>
    TimeAndMaterials = 1,
    /// <summary>Fixed-bid: invoice = quoted price; time entries are cost-tracking only.</summary>
    FixedBid = 2,
    /// <summary>Retainer: client buys hours up front; time entries debit Job.RetainerBalanceHours.</summary>
    Retainer = 3
}
