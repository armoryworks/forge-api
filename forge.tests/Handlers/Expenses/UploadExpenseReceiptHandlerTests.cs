using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.Expenses;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Tests.Handlers.Expenses;

public class UploadExpenseReceiptHandlerTests
{
    [Fact]
    public async Task Handle_EnsuresTheReceiptsBucketBeforeUploading()
    {
        var options = new MinioOptions();
        var calls = new List<string>();
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.EnsureBucketExistsAsync(options.ReceiptsBucket, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("ensure"))
            .Returns(Task.CompletedTask);
        storage.Setup(s => s.UploadAsync(options.ReceiptsBucket, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("upload"))
            .Returns(Task.CompletedTask);

        var files = new Mock<IFileRepository>();
        files.Setup(f => f.GetByEntityAsync("expenses", 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FileAttachmentResponseModel(0, "r.pdf", "application/pdf", 3, "", "expenses", 0, 1, "", DateTimeOffset.UtcNow, null, null)]);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")])),
        };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(http);

        var file = new Mock<IFormFile>();
        file.SetupGet(f => f.FileName).Returns("r.pdf");
        file.SetupGet(f => f.ContentType).Returns("application/pdf");
        file.SetupGet(f => f.Length).Returns(3);
        file.Setup(f => f.OpenReadStream()).Returns(new MemoryStream([1, 2, 3]));

        var handler = new UploadExpenseReceiptHandler(storage.Object, files.Object, accessor.Object, Options.Create(options));

        await handler.Handle(new UploadExpenseReceiptCommand(file.Object), default);

        calls.Should().Equal("ensure", "upload");
    }
}
