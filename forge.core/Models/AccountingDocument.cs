using Forge.Core.Enums;

namespace Forge.Core.Models;

public record AccountingDocument(
    AccountingDocumentType Type,
    string CustomerExternalId,
    List<AccountingLineItem> LineItems,
    string? RefNumber,
    decimal Amount,
    DateTimeOffset Date);
