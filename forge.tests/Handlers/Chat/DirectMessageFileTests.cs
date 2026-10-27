using System.Security.Claims;
using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Authorization;
using Forge.Api.Features.Chat;
using Forge.Api.Hubs;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Chat;

public class DirectMessageFileTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IFileRepository> _fileRepo = new();
    private readonly int _senderId;
    private readonly int _recipientId;
    private readonly int _outsiderId;
    private FileAttachment? _saved;

    public DirectMessageFileTests()
    {
        _senderId = AddUser("Sam");
        _recipientId = AddUser("Rae");
        _outsiderId = AddUser("Oli");
        _fileRepo.Setup(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Callback<FileAttachment, CancellationToken>((a, _) =>
            {
                _db.FileAttachments.Add(a);
                _db.SaveChanges();
                _saved = a;
            })
            .Returns(Task.CompletedTask);
        _fileRepo.Setup(r => r.GetByEntityAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string et, int id, CancellationToken _) =>
            [
                new FileAttachmentResponseModel(
                    _saved!.Id, _saved.FileName, _saved.ContentType, _saved.Size, $"/api/v1/files/{_saved.Id}/download",
                    et, id, _senderId, "Sam, Test", DateTimeOffset.UnixEpoch, null, null),
            ]);
    }

    private int AddUser(string first)
    {
        var user = new ApplicationUser { UserName = $"{first}@t.test", Email = $"{first}@t.test", FirstName = first, LastName = "Test" };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user.Id;
    }

    private static ClaimsPrincipal As(int userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "ProductionWorker")], "Test"));

    private UploadDirectMessageFileHandler Handler(int callerId)
    {
        var http = new Mock<IHttpContextAccessor>();
        http.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = As(callerId) });
        return new UploadDirectMessageFileHandler(
            _db, _storage.Object, _fileRepo.Object, http.Object,
            Options.Create(new MinioOptions { JobFilesBucket = "forge-job-files" }));
    }

    private static IFormFile TextFile(long length = 5, string name = "notes.txt")
    {
        var bytes = Encoding.UTF8.GetBytes("bytes");
        return new FormFile(new MemoryStream(bytes), 0, length, "file", name) { Headers = new HeaderDictionary(), ContentType = "text/plain" };
    }

    private static IHubContext<ChatHub> Hub()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(new Mock<IClientProxy>().Object);
        var hub = new Mock<IHubContext<ChatHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        return hub.Object;
    }

    [Fact]
    public async Task The_file_is_filed_against_the_recipient_and_uploaded_by_the_sender()
    {
        var result = await Handler(_senderId).Handle(new UploadDirectMessageFileCommand(_recipientId, TextFile()), CancellationToken.None);

        result.Id.Should().Be(_saved!.Id);
        _saved.EntityType.Should().Be(ChatFileRules.DirectMessageEntityType);
        _saved.EntityId.Should().Be(_recipientId);
        _saved.UploadedById.Should().Be(_senderId);
        _storage.Verify(s => s.UploadAsync("forge-job-files", It.Is<string>(k => k.StartsWith("chat-dm/")), It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Only_the_two_participants_can_read_the_file()
    {
        await Handler(_senderId).Handle(new UploadDirectMessageFileCommand(_recipientId, TextFile()), CancellationToken.None);
        var resolver = new EntityAccessResolver(_db);

        (await resolver.CanAccessFileAsync(_saved!, As(_recipientId), false, CancellationToken.None)).Should().BeTrue();
        (await resolver.CanAccessFileAsync(_saved!, As(_senderId), false, CancellationToken.None)).Should().BeTrue();
        (await resolver.CanAccessFileAsync(_saved!, As(_outsiderId), false, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Sending_a_file_to_yourself_is_refused()
    {
        var upload = () => Handler(_senderId).Handle(new UploadDirectMessageFileCommand(_senderId, TextFile()), CancellationToken.None);

        await upload.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData(10 * 1024 * 1024 + 1, "a.txt", false)]
    [InlineData(10 * 1024 * 1024, "a.txt", true)]
    [InlineData(5, "a.exe", false)]
    public void The_channel_size_and_type_limits_apply(long length, string name, bool valid)
    {
        new UploadDirectMessageFileCommandValidator()
            .Validate(new UploadDirectMessageFileCommand(2, TextFile(length, name)))
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task A_direct_message_carries_its_own_attachment()
    {
        await Handler(_senderId).Handle(new UploadDirectMessageFileCommand(_recipientId, TextFile()), CancellationToken.None);

        var sent = await new SendMessageHandler(_db, Hub())
            .Handle(new SendMessageCommand(_senderId, _recipientId, "", _saved!.Id), CancellationToken.None);

        sent.FileAttachment!.Id.Should().Be(_saved.Id);
    }

    [Fact]
    public async Task A_direct_message_cannot_borrow_someone_elses_file()
    {
        await Handler(_senderId).Handle(new UploadDirectMessageFileCommand(_recipientId, TextFile()), CancellationToken.None);

        var send = () => new SendMessageHandler(_db, Hub())
            .Handle(new SendMessageCommand(_outsiderId, _recipientId, "look", _saved!.Id), CancellationToken.None);

        await send.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public void A_message_with_a_file_may_have_no_text()
    {
        new SendMessageValidator().Validate(new SendMessageCommand(1, 2, "", 5)).IsValid.Should().BeTrue();
        new SendMessageValidator().Validate(new SendMessageCommand(1, 2, "")).IsValid.Should().BeFalse();
    }
}
