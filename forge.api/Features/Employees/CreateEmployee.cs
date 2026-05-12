using System.Text.Json;

using FluentValidation;

using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Employees;

/// <summary>
/// Phase 3 / WU-19 / F9 — create an Employee record without requiring a User
/// account. Supports the HR-first / IT-later workflow: HR onboards the
/// employee (hire date, department, pay info) before IT provisions a system
/// account. The Employee can later be promoted to also have a User via
/// <c>POST /api/v1/employees/{id}/grant-system-access</c>.
/// </summary>
public record CreateEmployeeCommand(
    string FirstName,
    string LastName,
    DateTimeOffset? HireDate,
    string? Department,
    string? JobTitle,
    string? EmployeeNumber,
    string? WorkEmail,
    string? PhoneNumber,
    PayInfoModel? PayInfo) : IRequest<EmployeeProfileResponseModel>;

public record PayInfoModel(decimal? Rate, string? Type);

public class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Department).MaximumLength(200);
        RuleFor(x => x.JobTitle).MaximumLength(200);
        RuleFor(x => x.EmployeeNumber).MaximumLength(50);
        RuleFor(x => x.WorkEmail).EmailAddress().MaximumLength(256)
            .When(x => !string.IsNullOrEmpty(x.WorkEmail));
        RuleFor(x => x.PhoneNumber).MaximumLength(50);
    }
}

public class CreateEmployeeHandler(
    AppDbContext db,
    ISystemAuditWriter audit,
    IHttpContextAccessor httpContext)
    : IRequestHandler<CreateEmployeeCommand, EmployeeProfileResponseModel>
{
    public async Task<EmployeeProfileResponseModel> Handle(CreateEmployeeCommand request, CancellationToken ct)
    {
        var profile = new Core.Entities.EmployeeProfile
        {
            UserId = null,
            FirstName = request.FirstName,
            LastName = request.LastName,
            WorkEmail = request.WorkEmail,
            PhoneNumber = request.PhoneNumber,
            StartDate = request.HireDate,
            Department = request.Department,
            JobTitle = request.JobTitle,
            EmployeeNumber = request.EmployeeNumber,
        };

        if (request.PayInfo is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.PayInfo.Type)
                && Enum.TryParse<PayType>(request.PayInfo.Type, ignoreCase: true, out var pt))
            {
                profile.PayType = pt;
            }
            if (request.PayInfo.Rate is not null)
            {
                if (profile.PayType == PayType.Salary)
                    profile.SalaryAmount = request.PayInfo.Rate;
                else
                    profile.HourlyRate = request.PayInfo.Rate;
            }
        }

        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync(ct);

        var actorId = TryGetActorId(httpContext);
        await audit.WriteAsync(
            "EmployeeCreated",
            actorId,
            entityType: "EmployeeProfile",
            entityId: profile.Id,
            details: JsonSerializer.Serialize(new
            {
                hasUserAccount = false,
                profile.FirstName,
                profile.LastName,
                profile.Department,
                profile.JobTitle,
            }),
            ct: ct);

        return Project(profile);
    }

    private static int TryGetActorId(IHttpContextAccessor http)
    {
        var v = http.HttpContext?.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(v, out var id) ? id : 0;
    }

    internal static EmployeeProfileResponseModel Project(Core.Entities.EmployeeProfile profile) =>
        new(
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
