namespace Forge.Core.Models;

public record SendQuoteEmailRequestModel(
    string RecipientEmail,
    string? Message);
