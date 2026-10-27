namespace Forge.Core.Models;

/// <summary>One characteristic on a certificate of analysis.</summary>
public record CoaResultResponseModel(
    int Id,
    int SortOrder,
    string Characteristic,
    string? Specification,
    decimal? SpecMin,
    decimal? SpecMax,
    decimal? SpecTarget,
    string? Result,
    string? Unit,
    string? Method,
    bool Pass,
    string SourceType,
    int? SourceId);
