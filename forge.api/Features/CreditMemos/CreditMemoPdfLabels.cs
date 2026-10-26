using Forge.Api.Services;

namespace Forge.Api.Features.CreditMemos;

/// <summary>The printed text of the credit memo PDF, in English or Spanish (es-US).</summary>
public sealed record CreditMemoPdfLabels(
    string Title,
    string CreditTo,
    string CreditDate,
    string InvoiceNumber,
    string Reason,
    string Voided,
    string Description,
    string Quantity,
    string UnitPrice,
    string Credit,
    string Subtotal,
    string Tax,
    string TotalCredit,
    string AppliedToInvoices,
    string Refunded,
    string CreditRemaining,
    string Notes,
    string Page,
    string Of,
    string DateFormat)
{
    public static readonly CreditMemoPdfLabels English = new(
        "CREDIT MEMO", "Credit To:", "Credit Date:", "Invoice #:", "Reason:", "VOIDED",
        "Description", "Qty", "Unit Price", "Credit",
        "Subtotal:", "Tax ({0}):", "Total credit:", "Applied to invoices:", "Refunded:", "Credit remaining:",
        "Notes", "Page ", " of ", "MM/dd/yyyy");

    public static readonly CreditMemoPdfLabels Spanish = new(
        "NOTA DE CRÉDITO", "Acreditar a:", "Fecha del crédito:", "Factura n.º:", "Motivo:", "ANULADA",
        "Descripción", "Cant.", "Precio unitario", "Crédito",
        "Subtotal:", "Impuesto ({0}):", "Crédito total:", "Aplicado a facturas:", "Reembolsado:", "Crédito restante:",
        "Notas", "Página ", " de ", "MM/dd/yyyy");

    public static CreditMemoPdfLabels For(string language)
        => language == DocumentLanguage.Spanish ? Spanish : English;
}
