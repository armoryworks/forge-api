using System.Globalization;

using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.SalesOrders;

public record CreateJobsForSalesOrderLinesCommand(
    int SalesOrderId, IReadOnlyCollection<int>? LineIds = null, bool FromConfirmation = false)
    : IRequest<CreateJobsForSalesOrderLinesResponseModel>;

public class CreateJobsForSalesOrderLinesHandler(
    AppDbContext db,
    IJobRepository jobRepo,
    IBarcodeService barcodeService,
    IBusinessIdentifierService identifiers,
    IHubContext<BoardHub> boardHub,
    ISalesOrderAcceptanceGate acceptanceGate,
    IMediator mediator,
    ICloudFolderAutoCreator folderAutoCreator)
    : IRequestHandler<CreateJobsForSalesOrderLinesCommand, CreateJobsForSalesOrderLinesResponseModel>
{
    private const int MaxTitleLength = 200;

    private static readonly SalesOrderStatus[] OpenStatuses =
        [SalesOrderStatus.Confirmed, SalesOrderStatus.InProduction, SalesOrderStatus.PartiallyShipped];

    public async Task<CreateJobsForSalesOrderLinesResponseModel> Handle(
        CreateJobsForSalesOrderLinesCommand request, CancellationToken cancellationToken)
    {
        var so = await db.SalesOrders.AsNoTracking()
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .FirstOrDefaultAsync(s => s.Id == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales order {request.SalesOrderId} not found.");

        if (!OpenStatuses.Contains(so.Status))
            throw new InvalidOperationException(
                $"Sales order {so.OrderNumber} is {so.Status}. Jobs can only be created for a confirmed, open order.");

        await acceptanceGate.EnsureReleasableAsync(so.Id, cancellationToken);

        var (track, startStage) = await ProductionTrackResolver.ResolveAsync(db, cancellationToken)
            ?? throw new InvalidOperationException(
                "No production track is set up, so no work orders can be created. Set one up in Admin first.");

        var lines = so.Lines.OrderBy(l => l.LineNumber).ToList();
        var explicitLines = request.LineIds is { Count: > 0 };
        if (explicitLines)
        {
            var unknown = request.LineIds!.Where(id => lines.All(l => l.Id != id)).ToList();
            if (unknown.Count > 0)
                throw new KeyNotFoundException(
                    $"Sales order line {unknown[0]} is not on sales order {so.OrderNumber}.");
            lines = lines.Where(l => request.LineIds!.Contains(l.Id)).ToList();
        }

        var lineIds = lines.Select(l => l.Id).ToList();
        JobDisposition[] uncoveringDispositions = explicitLines
            ? [JobDisposition.EnteredInError, JobDisposition.Scrap]
            : [JobDisposition.EnteredInError];
        var linkedLineIds = (await db.Jobs
            .Where(j => j.SalesOrderLineId.HasValue && lineIds.Contains(j.SalesOrderLineId.Value))
            .Select(j => new { LineId = j.SalesOrderLineId!.Value, j.Disposition })
            .ToListAsync(cancellationToken))
            .Where(j => j.Disposition is not JobDisposition d || !uncoveringDispositions.Contains(d))
            .Select(j => j.LineId)
            .ToHashSet();

        var partIds = lines.Where(l => l.PartId.HasValue).Select(l => l.PartId!.Value).Distinct().ToList();
        var routedPartIds = (await db.Operations
            .Where(o => partIds.Contains(o.PartId))
            .Select(o => o.PartId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var skipped = new List<SkippedSalesOrderLineResponseModel>();
        var toCreate = new List<SalesOrderLine>();
        foreach (var line in lines)
        {
            if (linkedLineIds.Contains(line.Id))
            {
                if (explicitLines)
                    skipped.Add(new SkippedSalesOrderLineResponseModel(line.LineNumber, "Already has a job"));
                continue;
            }

            if (line.IsFullyShipped)
            {
                skipped.Add(new SkippedSalesOrderLineResponseModel(line.LineNumber, "Already shipped"));
                continue;
            }

            var reason = SkipReason(line, routedPartIds);
            if (reason is not null)
                skipped.Add(new SkippedSalesOrderLineResponseModel(line.LineNumber, reason));
            else
                toCreate.Add(line);
        }

        var revisionsByPart = await db.Parts.AsNoTracking()
            .Where(p => partIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CurrentBomRevisionId, p.Revision })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var boardPosition = await jobRepo.GetMaxBoardPositionAsync(startStage.Id, cancellationToken);
        var created = new List<Job>();
        foreach (var line in toCreate)
        {
            var partId = line.PartId!.Value;
            var quantity = line.RemainingQuantity;
            var jobNumber = await jobRepo.GenerateNextJobNumberAsync(cancellationToken);

            var job = new Job
            {
                JobNumber = jobNumber,
                Title = JobTitle(so, line, quantity),
                Description = $"Created from Sales Order {so.OrderNumber}, Line {line.LineNumber}. Qty: {FormatQuantity(quantity)}.",
                TrackTypeId = track.Id,
                CurrentStageId = startStage.Id,
                SalesOrderLineId = line.Id,
                PartId = partId,
                BomRevisionIdAtRelease = revisionsByPart.GetValueOrDefault(partId)?.CurrentBomRevisionId,
                PartRevision = revisionsByPart.GetValueOrDefault(partId)?.Revision,
                CustomerId = so.CustomerId,
                Priority = JobPriority.Normal,
                DueDate = so.RequestedDeliveryDate,
                BoardPosition = ++boardPosition,
            };

            job.JobParts.Add(new JobPart
            {
                PartId = partId,
                Quantity = quantity,
                Notes = $"From {so.OrderNumber} line {line.LineNumber}",
            });

            job.ActivityLogs.Add(new JobActivityLog
            {
                Action = ActivityAction.Created,
                Description = $"Job {jobNumber} created from Sales Order {so.OrderNumber}, Line {line.LineNumber}.",
            });

            db.Jobs.Add(job);
            created.Add(job);
        }

        if (created.Count > 0 || (request.FromConfirmation && skipped.Count > 0))
            db.LogActivityAt(
                request.FromConfirmation ? "jobs_auto_created" : "jobs_created",
                ActivityDescription(so.OrderNumber, created.Count, skipped), ("SalesOrder", so.Id));

        await db.SaveChangesAsync(cancellationToken);

        foreach (var job in created)
        {
            await barcodeService.CreateBarcodeAsync(BarcodeEntityType.Job, job.Id, job.JobNumber, cancellationToken);
            await identifiers.IssueAsync(BusinessEntityType.Job, job.Id, job.JobNumber, cancellationToken);
            await boardHub.Clients.Group($"board:{track.Id}")
                .SendAsync("jobCreated", new BoardJobCreatedEvent(
                    job.Id, job.JobNumber, job.Title, track.Id,
                    startStage.Id, startStage.Name, job.BoardPosition), cancellationToken);
        }

        if (created.Count == 0)
            return new CreateJobsForSalesOrderLinesResponseModel(created.Count, skipped);

        var userId = db.CurrentUserId ?? 0;
        var customerName = await db.Customers.AsNoTracking()
            .Where(c => c.Id == so.CustomerId)
            .Select(c => string.IsNullOrWhiteSpace(c.CompanyName) ? c.Name : $"{c.Name} ({c.CompanyName})")
            .FirstOrDefaultAsync(cancellationToken);
        foreach (var job in created)
        {
            if (userId > 0)
                await mediator.Publish(new JobCreatedEvent(job.Id, userId), cancellationToken);

            var tokenContext = new Dictionary<string, string> { ["Job"] = job.JobNumber };
            if (!string.IsNullOrEmpty(customerName))
                tokenContext["Customer"] = customerName;
            await folderAutoCreator.AutoCreateAsync("Job", job.Id, tokenContext, cancellationToken);
        }

        return new CreateJobsForSalesOrderLinesResponseModel(created.Count, skipped);
    }

    private static string? SkipReason(SalesOrderLine line, HashSet<int> routedPartIds)
    {
        if (line.PartId is not int partId || line.Part is null)
            return "No part on this line";

        if (routedPartIds.Contains(partId))
            return null;

        return line.Part.ProcurementSource switch
        {
            ProcurementSource.Buy => "Bought part with no routing",
            ProcurementSource.Phantom => "Phantom part with no routing",
            _ => null,
        };
    }

    private static string JobTitle(SalesOrder so, SalesOrderLine line, decimal quantity)
    {
        var title = !string.IsNullOrWhiteSpace(line.Part?.PartNumber)
            ? $"{line.Part.PartNumber} x {FormatQuantity(quantity)}"
            : !string.IsNullOrWhiteSpace(line.Description)
                ? line.Description
                : $"{so.OrderNumber} Line {line.LineNumber}";
        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }

    private static string FormatQuantity(decimal quantity) =>
        quantity.ToString("0.####", CultureInfo.InvariantCulture);

    private static string ActivityDescription(
        string orderNumber, int createdCount, List<SkippedSalesOrderLineResponseModel> skipped)
    {
        var description = $"{createdCount} job(s) created for Sales Order {orderNumber}.";
        if (skipped.Count == 0)
            return description;

        var skips = string.Join(", ", skipped.Select(s => $"line {s.LineNumber} ({s.Reason.ToLowerInvariant()})"));
        return $"{description} Skipped {skips}.";
    }
}
