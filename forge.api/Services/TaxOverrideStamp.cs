using Forge.Core.Enums;

namespace Forge.Api.Services;

/// <summary>An accepted tax-rate override, as stamped on the quote, sales order or invoice it applies to.</summary>
public sealed record TaxOverrideStamp(TaxOverrideReason Reason, string? Note, int? TaxDocumentId);
