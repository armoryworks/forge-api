using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Inventory;

public record RecordInspectionResultCommand(int ReceivingRecordId, InspectionResultRequestModel Data) : IRequest;

public class RecordInspectionResultValidator : AbstractValidator<RecordInspectionResultCommand>
{
    public RecordInspectionResultValidator()
    {
        RuleFor(x => x.Data.Result).NotEmpty();
    }
}

public class RecordInspectionResultHandler(AppDbContext db, IHttpContextAccessor httpContext)
    : IRequestHandler<RecordInspectionResultCommand>
{
    public async Task Handle(RecordInspectionResultCommand request, CancellationToken ct)
    {
        var record = await db.ReceivingRecords.FindAsync([request.ReceivingRecordId], ct)
            ?? throw new KeyNotFoundException($"ReceivingRecord {request.ReceivingRecordId} not found.");

        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        if (!Enum.TryParse<ReceivingInspectionStatus>(request.Data.Result, out var status))
            throw new InvalidOperationException($"Invalid inspection status: {request.Data.Result}");

        record.InspectionStatus = status;
        record.InspectedById = userId;
        record.InspectedAt = DateTimeOffset.UtcNow;
        record.InspectionNotes = request.Data.Notes?.Trim();
        record.InspectedQuantityAccepted = request.Data.AcceptedQuantity;
        record.InspectedQuantityRejected = request.Data.RejectedQuantity;
        record.QcInspectionId = request.Data.QcInspectionId;

        await db.SaveChangesAsync(ct);
    }
}
