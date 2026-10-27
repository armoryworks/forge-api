using FluentValidation;

namespace Forge.Api.Features.Reviews;

public static class ReviewCycleRules
{
    public const int MinScale = 3;
    public const int MaxScale = 10;

    public static void Apply<T>(
        AbstractValidator<T> validator,
        Func<T, string> name,
        Func<T, DateOnly> start,
        Func<T, DateOnly> end,
        Func<T, int?> scale,
        Func<T, IReadOnlyList<string>?> competencies,
        Func<T, string?> scopeType,
        Func<T, string?> department,
        Func<T, IReadOnlyList<int>?> userIds)
    {
        validator.RuleFor(x => name(x)).NotEmpty().MaximumLength(200).OverridePropertyName("Name");
        validator.RuleFor(x => start(x)).NotEmpty().OverridePropertyName("StartDate");
        validator.RuleFor(x => end(x)).Must((x, e) => e > start(x)).OverridePropertyName("EndDate")
            .WithMessage("End date must be after the start date.");
        validator.RuleFor(x => scale(x)).InclusiveBetween(MinScale, MaxScale).When(x => scale(x).HasValue)
            .OverridePropertyName("RatingScaleMax");
        validator.RuleFor(x => competencies(x)).Must(c => c is null || (c.Count <= 30 && c.All(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 100)))
            .OverridePropertyName("Competencies").WithMessage("Up to 30 competencies, each named and at most 100 characters.");
        validator.RuleFor(x => scopeType(x)).Must(s => s is null || ReviewScopeTypes.Values.Contains(s))
            .OverridePropertyName("ScopeType").WithMessage("Scope is all, department or list.");
        validator.RuleFor(x => department(x)).NotEmpty().When(x => scopeType(x) == ReviewScopeTypes.Department)
            .OverridePropertyName("ScopeDepartment");
        validator.RuleFor(x => userIds(x)).Must(ids => ids is { Count: > 0 }).When(x => scopeType(x) == ReviewScopeTypes.List)
            .OverridePropertyName("ScopeUserIds").WithMessage("Choose at least one employee.");
    }

    public static List<string>? CleanCompetencies(IReadOnlyList<string>? competencies)
        => competencies?.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
