using MediatR;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record SetJobCoverPhotoCommand(int JobId, int? FileAttachmentId) : IRequest;

public class SetJobCoverPhotoHandler(AppDbContext db) : IRequestHandler<SetJobCoverPhotoCommand>
{
    public async Task Handle(SetJobCoverPhotoCommand request, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.FindAsync([request.JobId], cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        job.CoverPhotoFileId = request.FileAttachmentId;
        await db.SaveChangesAsync(cancellationToken);
    }
}
