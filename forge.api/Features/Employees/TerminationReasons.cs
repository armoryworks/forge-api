namespace Forge.Api.Features.Employees;

public static class TerminationReasons
{
    public const string GroupCode = "termination_reason";

    public static readonly IReadOnlyList<(string Code, string Label)> Seeded =
    [
        ("resignation", "Resignation"),
        ("dismissal", "Dismissal"),
        ("layoff", "Layoff"),
        ("retirement", "Retirement"),
        ("end_of_contract", "End of contract"),
        ("other", "Other"),
    ];
}
