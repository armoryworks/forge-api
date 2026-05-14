using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Jobs.Links;

public record CreateJobLinkCommand(
    int JobId,
    int TargetJobId,
    string LinkType) : IRequest<JobLinkResponseModel>;

public class CreateJobLinkValidator : AbstractValidator<CreateJobLinkCommand>
{
    public CreateJobLinkValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
        RuleFor(x => x.TargetJobId).GreaterThan(0);
        RuleFor(x => x.LinkType).NotEmpty().MaximumLength(50);
    }
}

public class CreateJobLinkHandler(
    IJobLinkRepository repo,
    IHubContext<BoardHub> boardHub) : IRequestHandler<CreateJobLinkCommand, JobLinkResponseModel>
{
    public async Task<JobLinkResponseModel> Handle(CreateJobLinkCommand request, CancellationToken cancellationToken)
    {
        if (request.JobId == request.TargetJobId)
            throw new InvalidOperationException("A job cannot be linked to itself.");

        if (!Enum.TryParse<JobLinkType>(request.LinkType, out var linkType))
            throw new InvalidOperationException($"Invalid link type: {request.LinkType}");

        var sourceExists = await repo.JobExistsAsync(request.JobId, cancellationToken);
        if (!sourceExists)
            throw new KeyNotFoundException($"Job with ID {request.JobId} not found.");

        var targetExists = await repo.JobExistsAsync(request.TargetJobId, cancellationToken);
        if (!targetExists)
            throw new KeyNotFoundException($"Job with ID {request.TargetJobId} not found.");

        var duplicate = await repo.LinkExistsAsync(request.JobId, request.TargetJobId, linkType, cancellationToken);
        if (duplicate)
            throw new InvalidOperationException("This link already exists.");

        var link = new JobLink
        {
            SourceJobId = request.JobId,
            TargetJobId = request.TargetJobId,
            LinkType = linkType,
        };

        await repo.AddAsync(link, cancellationToken);
        await repo.SaveChangesAsync(cancellationToken);

        // Re-fetch to get navigation properties populated
        var links = await repo.GetByJobIdAsync(request.JobId, cancellationToken);
        var result = links.First(l => l.Id == link.Id);

        // Notify both jobs' subscribers
        await boardHub.Clients.Group($"job:{request.JobId}")
            .SendAsync("linkChanged", new { jobId = request.JobId, link = result, changeType = "created" }, cancellationToken);
        await boardHub.Clients.Group($"job:{request.TargetJobId}")
            .SendAsync("linkChanged", new { jobId = request.TargetJobId, changeType = "created" }, cancellationToken);

        return result;
    }
}
