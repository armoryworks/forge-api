namespace Forge.Core.Models;

public record InspectionResultRequestModel(
    string Result,
    decimal? AcceptedQuantity,
    decimal? RejectedQuantity,
    string? Notes,
    bool CreateNcrOnReject = true,
    int? QcInspectionId = null);
