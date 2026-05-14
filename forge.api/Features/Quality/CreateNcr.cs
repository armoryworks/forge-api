using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record CreateNcrCommand(CreateNcrRequestModel Request) : IRequest<NcrResponseModel>;

public class CreateNcrValidator : AbstractValidator<CreateNcrCommand>
{
    public CreateNcrValidator()
    {
        RuleFor(x => x.Request.PartId).GreaterThan(0);
        RuleFor(x => x.Request.Description).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.Request.AffectedQuantity).GreaterThan(0);
        RuleFor(x => x.Request.DefectiveQuantity)
            .LessThanOrEqualTo(x => x.Request.AffectedQuantity)
            .When(x => x.Request.DefectiveQuantity.HasValue);
    }
}

public class CreateNcrHandler(
    AppDbContext db,
    INcrCapaService ncrCapaService,
    IClock clock,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<CreateNcrCommand, NcrResponseModel>
{
    public async Task<NcrResponseModel> Handle(
        CreateNcrCommand command, CancellationToken cancellationToken)
    {
        var req = command.Request;

        var part = await db.Parts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.PartId, cancellationToken)
            ?? throw new KeyNotFoundException($"Part {req.PartId} not found");

        var userId = int.Parse(httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var ncrNumber = await ncrCapaService.GenerateNcrNumberAsync(cancellationToken);

        var ncr = new NonConformance
        {
            NcrNumber = ncrNumber,
            Type = req.Type,
            PartId = req.PartId,
            JobId = req.JobId,
            ProductionRunId = req.ProductionRunId,
            LotNumber = req.LotNumber,
            SalesOrderLineId = req.SalesOrderLineId,
            PurchaseOrderLineId = req.PurchaseOrderLineId,
            QcInspectionId = req.QcInspectionId,
            DetectedById = userId,
            DetectedAt = clock.UtcNow,
            DetectedAtStage = req.DetectedAtStage,
            Description = req.Description,
            AffectedQuantity = req.AffectedQuantity,
            DefectiveQuantity = req.DefectiveQuantity,
            ContainmentActions = req.ContainmentActions,
            CustomerId = req.CustomerId,
            VendorId = req.VendorId,
            Status = NcrStatus.Open,
        };

        if (!string.IsNullOrWhiteSpace(req.ContainmentActions))
        {
            ncr.ContainmentById = userId;
            ncr.ContainmentAt = clock.UtcNow;
        }

        db.NonConformances.Add(ncr);
        await db.SaveChangesAsync(cancellationToken);

        var userName = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => $"{u.LastName}, {u.FirstName}")
            .FirstOrDefaultAsync(cancellationToken) ?? "Unknown";

        return new NcrResponseModel
        {
            Id = ncr.Id,
            NcrNumber = ncr.NcrNumber,
            Type = ncr.Type,
            PartId = ncr.PartId,
            PartNumber = part.PartNumber,
            PartDescription = part.Description ?? string.Empty,
            JobId = ncr.JobId,
            DetectedById = ncr.DetectedById,
            DetectedByName = userName,
            DetectedAt = ncr.DetectedAt,
            DetectedAtStage = ncr.DetectedAtStage,
            Description = ncr.Description,
            AffectedQuantity = ncr.AffectedQuantity,
            DefectiveQuantity = ncr.DefectiveQuantity,
            ContainmentActions = ncr.ContainmentActions,
            ContainmentById = ncr.ContainmentById,
            ContainmentByName = ncr.ContainmentById.HasValue ? userName : null,
            ContainmentAt = ncr.ContainmentAt,
            Status = ncr.Status,
            CustomerId = ncr.CustomerId,
            VendorId = ncr.VendorId,
            CreatedAt = ncr.CreatedAt,
        };
    }
}
