namespace Forge.Core.Models;

/// <summary>
/// The default recipient for an invoice email. <c>Source</c> says where it came from: <c>billing-contact</c>,
/// <c>customer</c> (the customer's own email) or <c>primary-contact</c>; all fields are null when the customer
/// has no email anywhere.
/// </summary>
public record InvoiceEmailRecipientResponseModel(string? Email, string? Source, string? ContactName);
