using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EmployeeProfile;

public record AdminUpdateEmployeeProfileCommand(
    int UserId,
    AdminUpdateEmployeeProfileRequestModel Data) : IRequest<EmployeeProfileResponseModel>;

public class AdminUpdateEmployeeProfileValidator : AbstractValidator<AdminUpdateEmployeeProfileCommand>
{
    public AdminUpdateEmployeeProfileValidator()
    {
        RuleFor(x => x.Data.Department).MaximumLength(200);
        RuleFor(x => x.Data.JobTitle).MaximumLength(200);
        RuleFor(x => x.Data.EmployeeNumber).MaximumLength(50);
        RuleFor(x => x.Data.HourlyRate).GreaterThanOrEqualTo(0).When(x => x.Data.HourlyRate.HasValue);
        RuleFor(x => x.Data.SalaryAmount).GreaterThanOrEqualTo(0).When(x => x.Data.SalaryAmount.HasValue);
    }
}

public class AdminUpdateEmployeeProfileHandler(AppDbContext db) : IRequestHandler<AdminUpdateEmployeeProfileCommand, EmployeeProfileResponseModel>
{
    public async Task<EmployeeProfileResponseModel> Handle(AdminUpdateEmployeeProfileCommand request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (profile is null)
        {
            profile = new Core.Entities.EmployeeProfile { UserId = request.UserId };
            db.EmployeeProfiles.Add(profile);
        }

        var data = request.Data;

        profile.StartDate = data.StartDate;
        profile.Department = data.Department;
        profile.JobTitle = data.JobTitle;
        profile.EmployeeNumber = data.EmployeeNumber;
        profile.PayType = data.PayType;
        profile.HourlyRate = data.HourlyRate;
        profile.SalaryAmount = data.SalaryAmount;
        profile.W4CompletedAt = data.W4CompletedAt;
        profile.StateWithholdingCompletedAt = data.StateWithholdingCompletedAt;
        profile.I9CompletedAt = data.I9CompletedAt;
        profile.I9ExpirationDate = data.I9ExpirationDate;
        profile.DirectDepositCompletedAt = data.DirectDepositCompletedAt;
        profile.WorkersCompAcknowledgedAt = data.WorkersCompAcknowledgedAt;
        profile.HandbookAcknowledgedAt = data.HandbookAcknowledgedAt;

        await db.SaveChangesAsync(ct);

        return new EmployeeProfileResponseModel(
            Id: profile.Id,
            UserId: profile.UserId,
            DateOfBirth: profile.DateOfBirth,
            Gender: profile.Gender,
            Street1: profile.Street1,
            Street2: profile.Street2,
            City: profile.City,
            State: profile.State,
            ZipCode: profile.ZipCode,
            Country: profile.Country,
            PhoneNumber: profile.PhoneNumber,
            PersonalEmail: profile.PersonalEmail,
            EmergencyContactName: profile.EmergencyContactName,
            EmergencyContactPhone: profile.EmergencyContactPhone,
            EmergencyContactRelationship: profile.EmergencyContactRelationship,
            StartDate: profile.StartDate,
            Department: profile.Department,
            JobTitle: profile.JobTitle,
            EmployeeNumber: profile.EmployeeNumber,
            PayType: profile.PayType,
            HourlyRate: profile.HourlyRate,
            SalaryAmount: profile.SalaryAmount,
            W4CompletedAt: profile.W4CompletedAt,
            StateWithholdingCompletedAt: profile.StateWithholdingCompletedAt,
            I9CompletedAt: profile.I9CompletedAt,
            I9ExpirationDate: profile.I9ExpirationDate,
            DirectDepositCompletedAt: profile.DirectDepositCompletedAt,
            WorkersCompAcknowledgedAt: profile.WorkersCompAcknowledgedAt,
            HandbookAcknowledgedAt: profile.HandbookAcknowledgedAt);
    }
}
