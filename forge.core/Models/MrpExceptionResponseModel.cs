
namespace Forge.Core.Models;

public record MrpExceptionResponseModel(
    int Id,
    int MrpRunId,
    int PartId,
    string PartNumber,
    string PartDescription,
    MrpExceptionType ExceptionType,
    string Message,
    string? SuggestedAction,
    bool IsResolved,
    int? ResolvedByUserId,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNotes
);
