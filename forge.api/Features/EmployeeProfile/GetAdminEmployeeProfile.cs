using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EmployeeProfile;

public record GetAdminEmployeeProfileQuery(int UserId) : IRequest<EmployeeProfileResponseModel>;

public class GetAdminEmployeeProfileHandler(AppDbContext db) : IRequestHandler<GetAdminEmployeeProfileQuery, EmployeeProfileResponseModel>
{
    public async Task<EmployeeProfileResponseModel> Handle(GetAdminEmployeeProfileQuery request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (profile is null)
        {
            return new EmployeeProfileResponseModel(
                Id: 0, UserId: request.UserId,
                DateOfBirth: null, Gender: null,
                Street1: null, Street2: null, City: null, State: null, ZipCode: null, Country: null,
                PhoneNumber: null, PersonalEmail: null,
                EmergencyContactName: null, EmergencyContactPhone: null, EmergencyContactRelationship: null,
                StartDate: null, Department: null, JobTitle: null, EmployeeNumber: null,
                PayType: null, HourlyRate: null, SalaryAmount: null,
                W4CompletedAt: null, StateWithholdingCompletedAt: null,
                I9CompletedAt: null, I9ExpirationDate: null,
                DirectDepositCompletedAt: null, WorkersCompAcknowledgedAt: null, HandbookAcknowledgedAt: null);
        }

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
