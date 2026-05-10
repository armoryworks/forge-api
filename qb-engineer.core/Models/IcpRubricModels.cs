namespace QBEngineer.Core.Models;

/// <summary>
/// Phase 1r / Batch 10 — ICP rubric definitions. Each rubric is a named
/// scoring scheme with dimensions; one rubric can be marked default and
/// drives the nightly LeadScore recompute. Dimensions are independent
/// rows with their own weights; the score is the sum of matched-dimension
/// weights normalized to 0-100.
/// </summary>
public record IcpRubricResponseModel(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    bool IsDefault,
    int DimensionCount,
    DateTimeOffset CreatedAt);

public record IcpRubricDetailResponseModel(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    bool IsDefault,
    List<IcpDimensionResponseModel> Dimensions,
    DateTimeOffset CreatedAt);

public record IcpDimensionResponseModel(
    int Id,
    int IcpRubricId,
    string FieldKey,
    string? Label,
    string? MatchSpec,
    int Weight);

public record CreateIcpRubricRequest(
    string Name,
    string? Description);

public record UpdateIcpRubricRequest(
    string? Name,
    string? Description,
    bool IsActive,
    bool IsDefault);

public record SaveIcpDimensionRequest(
    int? Id,
    string FieldKey,
    string? Label,
    string? MatchSpec,
    int Weight);
