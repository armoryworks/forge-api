using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record DisposeJobCommand(int Id, DisposeJobRequestModel Data) : IRequest<JobDetailResponseModel>;

public class DisposeJobCommandValidator : AbstractValidator<DisposeJobCommand>
{
    public DisposeJobCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Data.Notes).MaximumLength(2000).When(x => x.Data.Notes is not null);
    }
}

public class DisposeJobHandler(
    IJobRepository jobRepo,
    IAssetRepository assetRepo,
    IMediator mediator,
    AppDbContext db) : IRequestHandler<DisposeJobCommand, JobDetailResponseModel>
{
    public async Task<JobDetailResponseModel> Handle(DisposeJobCommand request, CancellationToken cancellationToken)
    {
        var job = await jobRepo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Job with ID {request.Id} not found.");

        if (job.Disposition.HasValue)
            throw new InvalidOperationException($"Job {job.JobNumber} has already been disposed as {job.Disposition}.");

        job.Disposition = request.Data.Disposition;
        job.DispositionNotes = request.Data.Notes?.Trim();
        job.DispositionAt = DateTimeOffset.UtcNow;

        if (request.Data.Disposition == JobDisposition.CapitalizeAsAsset)
        {
            // Find first associated part (if any) for SourcePartId
            var jobPart = await db.JobParts
                .Where(jp => jp.JobId == job.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var asset = new Asset
            {
                Name = job.Title,
                AssetType = AssetType.Tooling,
                Status = AssetStatus.Active,
                Notes = $"Capitalized from job {job.JobNumber}",
                SourceJobId = job.Id,
                SourcePartId = jobPart?.PartId,
            };

            await assetRepo.AddAsync(asset, cancellationToken);
        }
        else
        {
            await jobRepo.SaveChangesAsync(cancellationToken);
        }

        return await mediator.Send(new GetJobByIdQuery(job.Id), cancellationToken);
    }
}
