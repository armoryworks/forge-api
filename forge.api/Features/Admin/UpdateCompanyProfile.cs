using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

public record UpdateCompanyProfileCommand(
    string? Name,
    string? Phone,
    string? Email,
    string? Ein,
    string? Website,
    string? TimeZone = null) : IRequest<CompanyProfileResponseModel>;

public class UpdateCompanyProfileValidator : AbstractValidator<UpdateCompanyProfileCommand>
{
    public UpdateCompanyProfileValidator()
    {
        RuleFor(x => x.TimeZone)
            .Must(BookTimeZoneRules.IsKnownTimeZone)
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone))
            .WithMessage("The time zone is not a known IANA time zone.")
            .WithErrorCode("time-zone-unknown");
    }
}

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
            [BookTimeZoneRules.CompanyTimeZoneKey] = request.TimeZone,
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
                        BookTimeZoneRules.CompanyTimeZoneKey => "Company time zone (IANA id); new books and the shop clock use it",
                        _ => null,
                    },
                });
            }
        }

        await db.SaveChangesAsync(ct);

        return await GetCompanyProfileHandler.ReadAsync(db, ct);
    }
}
