using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record UpdateCompanyProfileCommand(
    string? Name,
    string? Phone,
    string? Email,
    string? Ein,
    string? Website) : IRequest<CompanyProfileResponseModel>;

public class UpdateCompanyProfileHandler(AppDbContext db)
    : IRequestHandler<UpdateCompanyProfileCommand, CompanyProfileResponseModel>
{
    public async Task<CompanyProfileResponseModel> Handle(
        UpdateCompanyProfileCommand request, CancellationToken ct)
    {
        var settingMap = new Dictionary<string, string?>
        {
            ["company.name"] = request.Name,
            ["company.phone"] = request.Phone,
            ["company.email"] = request.Email,
            ["company.ein"] = request.Ein,
            ["company.website"] = request.Website,
        };

        foreach (var (key, value) in settingMap)
        {
            if (value == null) continue;

            var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (setting != null)
            {
                setting.Value = value.Trim();
            }
            else
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Key = key,
                    Value = value.Trim(),
                    Description = key switch
                    {
                        "company.name" => "Legal business name",
                        "company.phone" => "Main company phone",
                        "company.email" => "Main company email",
                        "company.ein" => "Federal tax identification number (EIN)",
                        "company.website" => "Company website URL",
                        _ => null,
                    },
                });
            }
        }

        await db.SaveChangesAsync(ct);

        var all = await db.SystemSettings
            .AsNoTracking()
            .Where(s => s.Key.StartsWith("company."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new CompanyProfileResponseModel(
            Name: all.GetValueOrDefault("company.name", ""),
            Phone: all.GetValueOrDefault("company.phone", ""),
            Email: all.GetValueOrDefault("company.email", ""),
            Ein: all.GetValueOrDefault("company.ein", ""),
            Website: all.GetValueOrDefault("company.website", ""));
    }
}
