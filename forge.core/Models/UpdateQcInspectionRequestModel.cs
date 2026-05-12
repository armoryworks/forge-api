namespace Forge.Core.Models;

public record UpdateQcInspectionRequestModel(
    string? Status,
    string? Notes,
    List<UpdateQcInspectionResultModel>? Results);
