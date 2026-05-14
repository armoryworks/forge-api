using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateFmeaCommand(CreateFmeaRequestModel Request) : IRequest<FmeaResponseModel>;

public class CreateFmeaValidator : AbstractValidator<CreateFmeaCommand>
{
    public CreateFmeaValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Type).IsInEnum();
    }
}

public class CreateFmeaHandler(AppDbContext db, IClock clock)
    : IRequestHandler<CreateFmeaCommand, FmeaResponseModel>
{
    public async Task<FmeaResponseModel> Handle(
        CreateFmeaCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;
        var prefix = req.Type == FmeaType.Design ? "FMEA-D" : "FMEA-P";

        var lastNumber = await db.FmeaAnalyses
            .Where(f => f.FmeaNumber.StartsWith(prefix))
            .OrderByDescending(f => f.Id)
            .Select(f => f.FmeaNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var nextSeq = 1;
        if (lastNumber != null)
        {
            var parts = lastNumber.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out var parsed))
                nextSeq = parsed + 1;
        }

        var fmea = new FmeaAnalysis
        {
            FmeaNumber = $"{prefix}-{nextSeq:D4}",
            Name = req.Name,
            Type = req.Type,
            PartId = req.PartId,
            OperationId = req.OperationId,
            Status = FmeaStatus.Draft,
            PreparedBy = req.PreparedBy,
            Responsibility = req.Responsibility,
            OriginalDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            RevisionNumber = 1,
            Notes = req.Notes,
            PpapSubmissionId = req.PpapSubmissionId,
        };

        db.FmeaAnalyses.Add(fmea);
        await db.SaveChangesAsync(cancellationToken);

        return new FmeaResponseModel
        {
            Id = fmea.Id,
            FmeaNumber = fmea.FmeaNumber,
            Name = fmea.Name,
            Type = fmea.Type,
            PartId = fmea.PartId,
            OperationId = fmea.OperationId,
            Status = fmea.Status,
            PreparedBy = fmea.PreparedBy,
            Responsibility = fmea.Responsibility,
            OriginalDate = fmea.OriginalDate,
            RevisionDate = fmea.RevisionDate,
            RevisionNumber = fmea.RevisionNumber,
            PpapSubmissionId = fmea.PpapSubmissionId,
            HighRpnCount = 0,
            MaxRpn = 0,
            Items = [],
            CreatedAt = fmea.CreatedAt,
        };
    }
}
