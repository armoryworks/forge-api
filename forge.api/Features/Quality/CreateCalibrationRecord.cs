using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateCalibrationRecordCommand(int GageId, CreateCalibrationRecordRequestModel Request) : IRequest<CalibrationRecordResponseModel>;

public class CreateCalibrationRecordValidator : AbstractValidator<CreateCalibrationRecordCommand>
{
    public CreateCalibrationRecordValidator()
    {
        RuleFor(x => x.Request.CalibratedAt).NotEmpty();
        RuleFor(x => x.Request.Result).IsInEnum();
    }
}

public class CreateCalibrationRecordHandler(AppDbContext db) : IRequestHandler<CreateCalibrationRecordCommand, CalibrationRecordResponseModel>
{
    public async Task<CalibrationRecordResponseModel> Handle(CreateCalibrationRecordCommand request, CancellationToken cancellationToken)
    {
        var gage = await db.Gages.FirstOrDefaultAsync(g => g.Id == request.GageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Gage {request.GageId} not found");

        var nextDue = DateOnly.FromDateTime(request.Request.CalibratedAt.UtcDateTime.AddDays(gage.CalibrationIntervalDays));

        var record = new CalibrationRecord
        {
            GageId = request.GageId,
            CalibratedById = 0, // Will be set by middleware/auth context
            CalibratedAt = request.Request.CalibratedAt,
            Result = request.Request.Result,
            LabName = request.Request.LabName?.Trim(),
            CertificateFileId = request.Request.CertificateFileId,
            StandardsUsed = request.Request.StandardsUsed?.Trim(),
            AsFoundCondition = request.Request.AsFoundCondition?.Trim(),
            AsLeftCondition = request.Request.AsLeftCondition?.Trim(),
            NextCalibrationDue = nextDue,
            Notes = request.Request.Notes?.Trim(),
        };

        db.CalibrationRecords.Add(record);

        // Update gage with latest calibration info
        gage.LastCalibratedAt = request.Request.CalibratedAt;
        gage.NextCalibrationDue = nextDue;
        gage.Status = request.Request.Result == CalibrationResult.Fail
            ? GageStatus.OutOfService
            : GageStatus.InService;

        await db.SaveChangesAsync(cancellationToken);

        return new CalibrationRecordResponseModel(
            record.Id, record.GageId, record.CalibratedById, record.CalibratedAt,
            record.Result, record.LabName, record.CertificateFileId, record.StandardsUsed,
            record.AsFoundCondition, record.AsLeftCondition, record.NextCalibrationDue,
            record.Notes);
    }
}
