namespace Forge.Core.Models;

public record ScanIssueContextJob(
    int JobId,
    string JobNumber,
    string Title,
    decimal RequiredQuantity,
    decimal IssuedQuantity,
    decimal RemainingQuantity);
