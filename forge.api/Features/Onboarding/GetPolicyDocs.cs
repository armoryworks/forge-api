using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Onboarding;

/// <summary>
/// Returns the policy-document URLs surfaced on the onboarding Acknowledgments
/// step. Reads from SystemSettings `hr.workersCompDocUrl` and `hr.handbookDocUrl`.
/// Blank values map to null so the UI can omit links and (for handbook) skip the
/// acknowledgment entirely.
/// </summary>
public record GetOnboardingPolicyDocsQuery() : IRequest<OnboardingPolicyDocsModel>;

public class GetOnboardingPolicyDocsHandler(AppDbContext db)
    : IRequestHandler<GetOnboardingPolicyDocsQuery, OnboardingPolicyDocsModel>
{
    public async Task<OnboardingPolicyDocsModel> Handle(
        GetOnboardingPolicyDocsQuery request, CancellationToken ct)
    {
        var settings = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key == "hr.workersCompDocUrl" || s.Key == "hr.handbookDocUrl")
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new OnboardingPolicyDocsModel(
            WorkersCompDocUrl: NullIfBlank(settings.GetValueOrDefault("hr.workersCompDocUrl")),
            HandbookDocUrl:    NullIfBlank(settings.GetValueOrDefault("hr.handbookDocUrl")));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
