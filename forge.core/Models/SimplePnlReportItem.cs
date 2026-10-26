namespace Forge.Core.Models;

public record SimplePnlReportItem(
    string Category,
    string Type,
    decimal Amount,
    string Basis = SimplePnlReportItem.InvoiceBasis)
{
    public const string InvoiceBasis = "Invoice";
    public const string LedgerBasis = "Ledger";
}
