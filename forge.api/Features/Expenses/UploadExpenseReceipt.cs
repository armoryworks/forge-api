using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Expenses;

// Pre-submission receipt upload. The FileAttachment is created with EntityId=0 and is
// re-associated to the Expense row after Create/Update runs.
public record UploadExpenseReceiptCommand(IFormFile File) : IRequest<FileAttachmentResponseModel>;

public class UploadExpenseReceiptValidator : AbstractValidator<UploadExpenseReceiptCommand>
{
    public UploadExpenseReceiptValidator()
    {
        RuleFor(x => x.File).NotNull().WithMessage("File is required.");
    }
}

public class UploadExpenseReceiptHandler(
    IStorageService storage,
    IFileRepository fileRepo,
    IHttpContextAccessor httpContext,
    IOptions<MinioOptions> minioOptions) : IRequestHandler<UploadExpenseReceiptCommand, FileAttachmentResponseModel>
{
    public async Task<FileAttachmentResponseModel> Handle(UploadExpenseReceiptCommand request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var file = request.File;

        var bucketName = minioOptions.Value.ReceiptsBucket;
        var objectKey = $"expenses/pending/{Guid.NewGuid():N}-{file.FileName}";

        await using var stream = file.OpenReadStream();
        await storage.UploadAsync(bucketName, objectKey, stream, file.ContentType, cancellationToken);

        var attachment = new FileAttachment
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            BucketName = bucketName,
            ObjectKey = objectKey,
            EntityType = "expenses",
            EntityId = 0,
            UploadedById = userId,
        };

        await fileRepo.AddAsync(attachment, cancellationToken);

        var files = await fileRepo.GetByEntityAsync("expenses", 0, cancellationToken);
        return files.First(f => f.Id == attachment.Id);
    }
}
