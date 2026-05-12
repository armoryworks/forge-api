using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreatePpapSubmissionCommand(CreatePpapSubmissionRequestModel Request) : IRequest<PpapSubmissionResponseModel>;

public class CreatePpapSubmissionValidator : AbstractValidator<CreatePpapSubmissionCommand>
{
    public CreatePpapSubmissionValidator()
    {
        RuleFor(x => x.Request.PartId).GreaterThan(0);
        RuleFor(x => x.Request.CustomerId).GreaterThan(0);
        RuleFor(x => x.Request.PpapLevel).InclusiveBetween(1, 5);
    }
}

public class CreatePpapSubmissionHandler(AppDbContext db)
    : IRequestHandler<CreatePpapSubmissionCommand, PpapSubmissionResponseModel>
{
    private static readonly string[] ElementNames =
    [
        "Design Records",
        "Engineering Change Documents",
        "Customer Engineering Approval",
        "Design FMEA",
        "Process Flow Diagrams",
        "Process FMEA",
        "Control Plan",
        "Measurement System Analysis",
        "Dimensional Results",
        "Material / Performance Test Results",
        "Initial Process Studies (SPC)",
        "Qualified Laboratory Documentation",
        "Appearance Approval Report",
        "Sample Production Parts",
        "Master Sample",
        "Checking Aids",
        "Customer-Specific Requirements",
        "Part Submission Warrant (PSW)",
    ];

    public async Task<PpapSubmissionResponseModel> Handle(
        CreatePpapSubmissionCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;

        var part = await db.Parts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {req.PartId} not found");

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.CustomerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Customer {req.CustomerId} not found");

        var lastNumber = await db.PpapSubmissions
            .OrderByDescending(s => s.Id)
            .Select(s => s.SubmissionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var nextSeq = 1;
        if (lastNumber != null && lastNumber.StartsWith("PPAP-") && int.TryParse(lastNumber[5..], out var parsed))
            nextSeq = parsed + 1;

        var submission = new PpapSubmission
        {
            SubmissionNumber = $"PPAP-{nextSeq:D4}",
            PartId = req.PartId,
            CustomerId = req.CustomerId,
            PpapLevel = req.PpapLevel,
            Status = PpapStatus.Draft,
            Reason = req.Reason,
            PartRevision = req.PartRevision,
            DueDate = req.DueDate,
            CustomerContactName = req.CustomerContactName,
            InternalNotes = req.InternalNotes,
        };

        // Auto-populate 18 elements based on level
        for (var i = 0; i < 18; i++)
        {
            submission.Elements.Add(new PpapElement
            {
                ElementNumber = i + 1,
                ElementName = ElementNames[i],
                Status = PpapElementStatus.NotStarted,
                IsRequired = IsElementRequired(i + 1, req.PpapLevel),
            });
        }

        db.PpapSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);

        return new PpapSubmissionResponseModel
        {
            Id = submission.Id,
            SubmissionNumber = submission.SubmissionNumber,
            PartId = submission.PartId,
            PartNumber = part.PartNumber,
            PartDescription = part.Description ?? string.Empty,
            CustomerId = submission.CustomerId,
            CustomerName = customer.Name,
            PpapLevel = submission.PpapLevel,
            Status = submission.Status,
            Reason = submission.Reason,
            PartRevision = submission.PartRevision,
            DueDate = submission.DueDate,
            CustomerContactName = submission.CustomerContactName,
            InternalNotes = submission.InternalNotes,
            CompletedElements = 0,
            RequiredElements = submission.Elements.Count(e => e.IsRequired),
            Elements = submission.Elements.OrderBy(e => e.ElementNumber).Select(e => new PpapElementResponseModel
            {
                Id = e.Id,
                ElementNumber = e.ElementNumber,
                ElementName = e.ElementName,
                Status = e.Status,
                IsRequired = e.IsRequired,
            }).ToList(),
            CreatedAt = submission.CreatedAt,
        };
    }

    private static bool IsElementRequired(int elementNumber, int level)
    {
        // PSW (element 18) is always required at all levels
        if (elementNumber == 18) return true;

        // Level 1: all elements required (retain)
        // Level 2: most required (submit for some, retain for others)
        // Level 3: all required (submit)
        // Level 4: all required (submit + retain)
        // Level 5: all required
        return level switch
        {
            1 => true,
            2 => true,
            3 => true,
            4 => true,
            5 => true,
            _ => true,
        };
    }
}
