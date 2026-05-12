namespace Forge.Core.Models;

public record UninvoicedJobResponseModel(
    int JobId,
    string JobNumber,
    string Title,
    string? CustomerName,
    DateTimeOffset CompletedDate,
    int? CustomerId);
