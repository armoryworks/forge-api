using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.EmployeeProfile;

public record GetEmployeeProfileQuery(int UserId) : IRequest<EmployeeProfileResponseModel>;

public class GetEmployeeProfileHandler(AppDbContext db) : IRequestHandler<GetEmployeeProfileQuery, EmployeeProfileResponseModel>
{
    public async Task<EmployeeProfileResponseModel> Handle(GetEmployeeProfileQuery request, CancellationToken ct)
    {
        var profile = await db.EmployeeProfiles
            .FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);

        if (profile is null)
        {
            try
            {
                profile = new Core.Entities.EmployeeProfile { UserId = request.UserId };
                db.EmployeeProfiles.Add(profile);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrent request already created the profile — reload
                db.ChangeTracker.Clear();
                profile = await db.EmployeeProfiles
                    .FirstAsync(p => p.UserId == request.UserId, ct);
            }
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
