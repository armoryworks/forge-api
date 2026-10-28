using Forge.Core.Settings;

namespace Forge.Api.Features.CreditMemos;

/// <summary>Sync-queue operations and settings for pushing credit memos and refunds to the connected accounting provider.</summary>
public static class CreditMemoSync
{
    public const string CreateCreditMemoOperation = "CreateCreditMemo";
    public const string CreateWriteOffOperation = "CreateCreditMemoWriteOff";
    public const string CreateRefundReceiptOperation = "CreateRefundReceipt";

    /// <summary>The QuickBooks item a write-off credit memo is booked against (an item pointing at bad debt).</summary>
    public const string BadDebtItemSettingKey = QuickBooksItemSettings.BadDebtItemKey;

    /// <summary>The QuickBooks item a refund receipt line is booked against.</summary>
    public const string RefundItemSettingKey = QuickBooksItemSettings.RefundItemKey;

    /// <summary>The QuickBooks bank account a refund receipt is paid from.</summary>
    public const string RefundAccountSettingKey = QuickBooksItemSettings.RefundAccountKey;

    public const string RefundMappingMissingMessage =
        "Map a QuickBooks item and bank account for refunds (settings qbo.refund_item_id and qbo.refund_account_id) before recording refunds in connected mode.";

    public const string ProviderUnsupportedMessage =
        "The connected accounting provider does not take credit memos or refunds from Forge; record them in the provider.";

    public const string BadDebtItemMissingMessage =
        "Map a QuickBooks item for bad debt (setting qbo.bad_debt_item_id) before writing off invoices in connected mode.";
}
