namespace Forge.Core.Models;

/// <summary>Payload for changing a Draft invoice's human-readable number.</summary>
public record RenameInvoiceNumberRequestModel(string InvoiceNumber);
