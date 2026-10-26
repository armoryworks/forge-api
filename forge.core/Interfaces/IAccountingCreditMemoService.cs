using Forge.Core.Models;

namespace Forge.Core.Interfaces;

/// <summary>
/// Optional provider capability: credit memos and refund receipts. A provider that does not implement it fails the
/// sync entry with a clear message instead of dropping the credit.
/// </summary>
public interface IAccountingCreditMemoService
{
    Task<string> CreateCreditMemoAsync(AccountingDocument document, CancellationToken ct);

    Task<string> CreateRefundReceiptAsync(AccountingRefund refund, CancellationToken ct);
}
