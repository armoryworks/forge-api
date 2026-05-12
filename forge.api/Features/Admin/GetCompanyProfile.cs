using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record GetCompanyProfileQuery : IRequest<CompanyProfileResponseModel>;

public class GetCompanyProfileHandler(AppDbContext db)
    : IRequestHandler<GetCompanyProfileQuery, CompanyProfileResponseModel>
{
    public async Task<CompanyProfileResponseModel> Handle(
        GetCompanyProfileQuery request, CancellationToken ct)
    {
        var settings = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith("company."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new CompanyProfileResponseModel(
            Name: settings.GetValueOrDefault("company.name", ""),
            Phone: settings.GetValueOrDefault("company.phone", ""),
            Email: settings.GetValueOrDefault("company.email", ""),
            Ein: settings.GetValueOrDefault("company.ein", ""),
            Website: settings.GetValueOrDefault("company.website", ""));
    }
}
