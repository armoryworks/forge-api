namespace Forge.Core.Models;

public record RagSearchResponseModel(
    List<RagSearchResultModel> Results,
    string? GeneratedAnswer);
