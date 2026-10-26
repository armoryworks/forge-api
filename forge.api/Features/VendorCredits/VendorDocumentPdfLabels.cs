namespace Forge.Api.Features.VendorCredits;

/// <summary>
/// The printed labels of the vendor credit memo and return-to-vendor documents, in English or Spanish. Any language
/// other than Spanish prints English.
/// </summary>
public sealed class VendorDocumentPdfLabels
{
    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["page"] = "Page", ["of"] = "of",
        ["creditMemo"] = "VENDOR CREDIT MEMO", ["debitMemo"] = "DEBIT MEMO",
        ["vendor"] = "Vendor:", ["memoDate"] = "Date:", ["bill"] = "Bill #:", ["return"] = "Return #:",
        ["vendorReference"] = "Vendor ref:", ["reason"] = "Reason:", ["voided"] = "VOIDED",
        ["number"] = "#", ["description"] = "Description", ["qty"] = "Qty", ["unitPrice"] = "Unit Price", ["amount"] = "Amount",
        ["subtotal"] = "Subtotal:", ["tax"] = "Tax:", ["totalCredit"] = "Total credit:",
        ["applied"] = "Applied to bills:", ["remaining"] = "Credit remaining:", ["notes"] = "Notes",
        ["rtv"] = "RETURN TO VENDOR", ["shipTo"] = "Ship to:", ["ncr"] = "NCR #:", ["po"] = "PO #:",
        ["rma"] = "Vendor RMA #:", ["shipDate"] = "Ship date:", ["carrier"] = "Carrier:", ["tracking"] = "Tracking #:",
        ["part"] = "Part", ["lot"] = "Lot", ["location"] = "From bin", ["expectedCredit"] = "Expected credit:",
        ["packedBy"] = "Packed by: ____________________", ["receivedBy"] = "Received by vendor: ____________________",
    };

    private static readonly Dictionary<string, string> Spanish = new(StringComparer.Ordinal)
    {
        ["page"] = "Página", ["of"] = "de",
        ["creditMemo"] = "NOTA DE CRÉDITO DEL PROVEEDOR", ["debitMemo"] = "NOTA DE DÉBITO",
        ["vendor"] = "Proveedor:", ["memoDate"] = "Fecha:", ["bill"] = "Factura n.º:", ["return"] = "Devolución n.º:",
        ["vendorReference"] = "Ref. del proveedor:", ["reason"] = "Motivo:", ["voided"] = "ANULADA",
        ["number"] = "#", ["description"] = "Descripción", ["qty"] = "Cant.", ["unitPrice"] = "Precio unitario", ["amount"] = "Importe",
        ["subtotal"] = "Subtotal:", ["tax"] = "Impuesto:", ["totalCredit"] = "Crédito total:",
        ["applied"] = "Aplicado a facturas:", ["remaining"] = "Crédito restante:", ["notes"] = "Notas",
        ["rtv"] = "DEVOLUCIÓN AL PROVEEDOR", ["shipTo"] = "Enviar a:", ["ncr"] = "NC n.º:", ["po"] = "OC n.º:",
        ["rma"] = "RMA del proveedor n.º:", ["shipDate"] = "Fecha de envío:", ["carrier"] = "Transportista:", ["tracking"] = "Rastreo n.º:",
        ["part"] = "Pieza", ["lot"] = "Lote", ["location"] = "Desde ubicación", ["expectedCredit"] = "Crédito esperado:",
        ["packedBy"] = "Empacado por: ____________________", ["receivedBy"] = "Recibido por el proveedor: ____________________",
    };

    private readonly Dictionary<string, string> labels;

    private VendorDocumentPdfLabels(Dictionary<string, string> labels, string dateFormat)
    {
        this.labels = labels;
        DateFormat = dateFormat;
    }

    /// <summary>The labels for <paramref name="language"/> (an ISO code such as "es" or "es-US").</summary>
    public static VendorDocumentPdfLabels For(string? language)
        => language is not null && language.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            ? new VendorDocumentPdfLabels(Spanish, "dd/MM/yyyy")
            : new VendorDocumentPdfLabels(English, "MM/dd/yyyy");

    public string DateFormat { get; }

    public string this[string key] => labels.TryGetValue(key, out var value) ? value : English[key];
}
