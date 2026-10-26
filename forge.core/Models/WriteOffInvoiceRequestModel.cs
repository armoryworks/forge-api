namespace Forge.Core.Models;

/// <summary>Writes off an invoice balance. <c>Amount</c> null writes off the whole balance due.</summary>
public record WriteOffInvoiceRequestModel(decimal? Amount, string Reason);
