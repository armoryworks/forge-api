using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EmployeeProfile;

public record UpdateEmployeeProfileCommand(
    int UserId,
    UpdateEmployeeProfileRequestModel Data) : IRequest<EmployeeProfileResponseModel>;

public class UpdateEmployeeProfileValidator : AbstractValidator<UpdateEmployeeProfileCommand>
{
    public UpdateEmployeeProfileValidator()
    {
        RuleFor(x => x.Data.PhoneNumber).MaximumLength(50);
        RuleFor(x => x.Data.PersonalEmail).MaximumLength(200).EmailAddress().When(x => !string.IsNullOrEmpty(x.Data.PersonalEmail));
        RuleFor(x => x.Data.Street1).MaximumLength(200);
        RuleFor(x => x.Data.Street2).MaximumLength(200);
        RuleFor(x => x.Data.City).MaximumLength(100);
        RuleFor(x => x.Data.State).MaximumLength(100);
        RuleFor(x => x.Data.ZipCode).MaximumLength(20);
        RuleFor(x => x.Data.Country).MaximumLength(100);
        RuleFor(x => x.Data.Gender).MaximumLength(50);
        RuleFor(x => x.Data.EmergencyContactName).MaximumLength(200);
        RuleFor(x => x.Data.EmergencyContactPhone).MaximumLength(50);
        RuleFor(x => x.Data.EmergencyContactRelationship).MaximumLength(100);
    }
}

public class UpdateEmployeeProfileHandler(AppDbContext db) : IRequestHandler<UpdateEmployeeProfileCommand, EmployeeProfileResponseModel>
{
    public async Task<EmployeeProfileResponseModel> Handle(UpdateEmployeeProfileCommand request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (profile is null)
        {
            profile = new Core.Entities.EmployeeProfile { UserId = request.UserId };
            db.EmployeeProfiles.Add(profile);
        }

        var data = request.Data;

        profile.DateOfBirth = data.DateOfBirth;
        profile.Gender = data.Gender;
        profile.Street1 = data.Street1;
        profile.Street2 = data.Street2;
        profile.City = data.City;
        profile.State = data.State;
        profile.ZipCode = data.ZipCode;
        profile.Country = data.Country;
        profile.PhoneNumber = data.PhoneNumber;
        profile.PersonalEmail = data.PersonalEmail;
        profile.EmergencyContactName = data.EmergencyContactName;
        profile.EmergencyContactPhone = data.EmergencyContactPhone;
        profile.EmergencyContactRelationship = data.EmergencyContactRelationship;

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
