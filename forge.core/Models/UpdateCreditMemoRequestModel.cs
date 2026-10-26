using Forge.Core.Enums;

namespace Forge.Core.Models;

public record UpdateCreditMemoRequestModel(
    CreditMemoReasonType ReasonType,
    string Reason,
    DateTimeOffset CreditDate,
    List<CreditMemoLineRequestModel> Lines,
    decimal? TaxRate = null,
    string? Notes = null);
