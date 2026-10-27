namespace Forge.Api.Features.Reviews;

public static class ReviewScopeTypes
{
    public const string All = "all";
    public const string Department = "department";
    public const string List = "list";

    public static readonly IReadOnlyList<string> Values = [All, Department, List];
}
