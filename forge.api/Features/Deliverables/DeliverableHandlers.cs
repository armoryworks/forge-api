using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Deliverables;

// ─── Create ─────────────────────────────────────────────────────────────────

public record CreateDeliverableCommand(CreateDeliverableRequestModel Request) : IRequest<DeliverableResponseModel>;

public class CreateDeliverableValidator : AbstractValidator<CreateDeliverableCommand>
{
    public CreateDeliverableValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Description).MaximumLength(4000);
        RuleFor(x => x.Request.DeliverableTypeId).GreaterThan(0);
    }
}

public class CreateDeliverableHandler(
    AppDbContext db,
    ICloudFolderAutoCreator folderAutoCreator)
    : IRequestHandler<CreateDeliverableCommand, DeliverableResponseModel>
{
    public async Task<DeliverableResponseModel> Handle(CreateDeliverableCommand command, CancellationToken ct)
    {
        var d = new Deliverable
        {
            Name = command.Request.Name,
            Description = command.Request.Description,
            JobId = command.Request.JobId,
            ProjectId = command.Request.ProjectId,
            CustomerId = command.Request.CustomerId,
            DeliverableTypeId = command.Request.DeliverableTypeId,
            Status = "Draft",
            DueDate = command.Request.DueDate,
            FileAttachmentIds = command.Request.FileAttachmentIds,
            CloudLinkExternalId = command.Request.CloudLinkExternalId,
        };
        db.Deliverables.Add(d);
        await db.SaveChangesAsync(ct);

        // Pro Services rollout (D2 dual-path) — best-effort cloud folder
        // auto-anchor. Loads parent Customer + Job names for token
        // resolution when those links are populated; otherwise the
        // resolver leaves the unmatched tokens literal.
        var tokenContext = new Dictionary<string, string>
        {
            ["Deliverable"] = d.Name,
        };
        if (d.JobId is int jobId)
        {
            var jobNumber = await db.Jobs
                .AsNoTracking()
                .Where(j => j.Id == jobId)
                .Select(j => j.JobNumber)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(jobNumber)) tokenContext["Job"] = jobNumber;
        }
        if (d.CustomerId is int custId)
        {
            var customerName = await db.Customers
                .AsNoTracking()
                .Where(c => c.Id == custId)
                .Select(c => string.IsNullOrWhiteSpace(c.CompanyName) ? c.Name : $"{c.Name} ({c.CompanyName})")
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrEmpty(customerName)) tokenContext["Customer"] = customerName;
        }
        await folderAutoCreator.AutoCreateAsync(
            entityType: "Deliverable", entityId: d.Id, tokenContext, ct);

        return MapResponse(d);
    }

    internal static DeliverableResponseModel MapResponse(Deliverable d) => new(
        Id: d.Id,
        Name: d.Name,
        Description: d.Description,
        JobId: d.JobId,
        ProjectId: d.ProjectId,
        CustomerId: d.CustomerId,
        DeliverableTypeId: d.DeliverableTypeId,
        Status: d.Status,
        DueDate: d.DueDate,
        DeliveredAt: d.DeliveredAt,
        DeliveredByUserId: d.DeliveredByUserId,
        FileAttachmentIds: d.FileAttachmentIds,
        CloudLinkExternalId: d.CloudLinkExternalId,
        CreatedAt: d.CreatedAt,
        UpdatedAt: d.UpdatedAt);
}

// ─── Update ─────────────────────────────────────────────────────────────────

public record UpdateDeliverableCommand(int Id, UpdateDeliverableRequestModel Request) : IRequest<DeliverableResponseModel>;

public class UpdateDeliverableValidator : AbstractValidator<UpdateDeliverableCommand>
{
    public UpdateDeliverableValidator()
    {
        RuleFor(x => x.Request.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.Status).NotEmpty().Must(BeKnownStatus)
            .WithMessage("Status must be one of: Draft, In Review, Approved, Delivered");
        RuleFor(x => x.Request.DeliverableTypeId).GreaterThan(0);
    }

    private static bool BeKnownStatus(string status) =>
        status is "Draft" or "In Review" or "Approved" or "Delivered";
}

public class UpdateDeliverableHandler(AppDbContext db)
    : IRequestHandler<UpdateDeliverableCommand, DeliverableResponseModel>
{
    public async Task<DeliverableResponseModel> Handle(UpdateDeliverableCommand command, CancellationToken ct)
    {
        var d = await db.Deliverables.FirstOrDefaultAsync(x => x.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"Deliverable {command.Id} not found");

        var wasDelivered = d.Status == "Delivered";
        var becomingDelivered = command.Request.Status == "Delivered" && !wasDelivered;

        d.Name = command.Request.Name;
        d.Description = command.Request.Description;
        d.JobId = command.Request.JobId;
        d.ProjectId = command.Request.ProjectId;
        d.CustomerId = command.Request.CustomerId;
        d.DeliverableTypeId = command.Request.DeliverableTypeId;
        d.Status = command.Request.Status;
        d.DueDate = command.Request.DueDate;
        d.FileAttachmentIds = command.Request.FileAttachmentIds;
        d.CloudLinkExternalId = command.Request.CloudLinkExternalId;

        if (becomingDelivered)
        {
            d.DeliveredAt = DateTimeOffset.UtcNow;
            d.DeliveredByUserId = db.CurrentUserId;
        }
        else if (wasDelivered && command.Request.Status != "Delivered")
        {
            // Reverting from Delivered: clear the delivery audit.
            d.DeliveredAt = null;
            d.DeliveredByUserId = null;
        }

        await db.SaveChangesAsync(ct);
        return CreateDeliverableHandler.MapResponse(d);
    }
}

// ─── Delete ─────────────────────────────────────────────────────────────────

public record DeleteDeliverableCommand(int Id) : IRequest;

public class DeleteDeliverableHandler(AppDbContext db) : IRequestHandler<DeleteDeliverableCommand>
{
    public async Task Handle(DeleteDeliverableCommand command, CancellationToken ct)
    {
        var d = await db.Deliverables.FirstOrDefaultAsync(x => x.Id == command.Id, ct)
            ?? throw new KeyNotFoundException($"Deliverable {command.Id} not found");
        // Soft delete via base entity; AppDbContext.SetTimestamps wires DeletedAt + DeletedBy.
        d.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

// ─── Get single ─────────────────────────────────────────────────────────────

public record GetDeliverableQuery(int Id) : IRequest<DeliverableResponseModel>;

public class GetDeliverableHandler(AppDbContext db) : IRequestHandler<GetDeliverableQuery, DeliverableResponseModel>
{
    public async Task<DeliverableResponseModel> Handle(GetDeliverableQuery query, CancellationToken ct)
    {
        var d = await db.Deliverables.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct)
            ?? throw new KeyNotFoundException($"Deliverable {query.Id} not found");
        return CreateDeliverableHandler.MapResponse(d);
    }
}

// ─── List ───────────────────────────────────────────────────────────────────

public record GetDeliverablesQuery(
    int? JobId,
    int? ProjectId,
    int? CustomerId,
    string? Status,
    int Page = 1,
    int PageSize = 25) : IRequest<DeliverableListResponseModel>;

public record DeliverableListResponseModel(
    IReadOnlyList<DeliverableResponseModel> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public class GetDeliverablesHandler(AppDbContext db) : IRequestHandler<GetDeliverablesQuery, DeliverableListResponseModel>
{
    public async Task<DeliverableListResponseModel> Handle(GetDeliverablesQuery query, CancellationToken ct)
    {
        var q = db.Deliverables.AsNoTracking();
        if (query.JobId is { } j) q = q.Where(x => x.JobId == j);
        if (query.ProjectId is { } p) q = q.Where(x => x.ProjectId == p);
        if (query.CustomerId is { } c) q = q.Where(x => x.CustomerId == c);
        if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(x => x.Status == query.Status);

        var total = await q.CountAsync(ct);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(1, query.Page);
        var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);

        var rows = await q.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new DeliverableListResponseModel(
            Items: rows.Select(CreateDeliverableHandler.MapResponse).ToList(),
            Page: page,
            PageSize: pageSize,
            TotalCount: total,
            TotalPages: totalPages);
    }
}

