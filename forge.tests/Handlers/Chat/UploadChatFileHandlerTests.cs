using System.Security.Claims;
using System.Text;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

using Forge.Api.Features.Chat;
using Forge.Api.Middleware;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Chat;

public class UploadChatFileHandlerTests
{
    private const int CallerId = 7;
    private const int OtherUserId = 8;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IStorageService> _storage = new();
    private readonly Mock<IFileRepository> _fileRepo = new();
    private readonly UploadChatFileHandler _handler;

    public UploadChatFileHandlerTests()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, CallerId.ToString())], "Test"));
        var httpContext = new Mock<IHttpContextAccessor>();
        httpContext.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = principal });

        _fileRepo.Setup(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Callback<FileAttachment, CancellationToken>((a, _) => a.Id = 42)
            .Returns(Task.CompletedTask);
        _fileRepo.Setup(r => r.GetByEntityAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string et, int id, CancellationToken _) =>
            [
                new FileAttachmentResponseModel(
                    42, "notes.txt", "text/plain", 5, "/api/v1/files/42/download",
                    et, id, CallerId, "Tester, QA", DateTimeOffset.UnixEpoch, null, null),
            ]);

        _handler = new UploadChatFileHandler(
            _db, _storage.Object, _fileRepo.Object, httpContext.Object,
            Options.Create(new MinioOptions { JobFilesBucket = "forge-job-files" }));
    }

    private async Task<int> RoomAsync(params int[] memberIds)
    {
        var room = new ChatRoom { Name = "floor", IsGroup = true, CreatedById = OtherUserId };
        _db.ChatRooms.Add(room);
        await _db.SaveChangesAsync();
        foreach (var userId in memberIds)
            _db.ChatRoomMembers.Add(new ChatRoomMember { ChatRoomId = room.Id, UserId = userId });
        await _db.SaveChangesAsync();
        return room.Id;
    }

    private static IFormFile TextFile()
    {
        var bytes = Encoding.UTF8.GetBytes("bytes");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "notes.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain",
        };
    }

    [Fact]
    public async Task A_member_uploads_into_the_channel()
    {
        var roomId = await RoomAsync(CallerId, OtherUserId);

        var result = await _handler.Handle(new UploadChatFileCommand(roomId, TextFile()), CancellationToken.None);

        result.Id.Should().Be(42);
        _storage.Verify(s => s.UploadAsync(
            "forge-job-files", It.Is<string>(k => k.StartsWith($"chat/{roomId}/")),
            It.IsAny<Stream>(), "text/plain", It.IsAny<CancellationToken>()), Times.Once);
        _fileRepo.Verify(r => r.AddAsync(
            It.Is<FileAttachment>(a => a.EntityType == "chat" && a.EntityId == roomId && a.UploadedById == CallerId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_non_member_is_forbidden_and_nothing_is_stored()
    {
        var roomId = await RoomAsync(OtherUserId);

        var act = () => _handler.Handle(new UploadChatFileCommand(roomId, TextFile()), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _storage.Verify(s => s.UploadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fileRepo.Verify(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unknown_channel_is_not_found_and_nothing_is_stored()
    {
        var roomId = await RoomAsync(CallerId);

        var act = () => _handler.Handle(new UploadChatFileCommand(roomId + 1, TextFile()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _storage.Verify(s => s.UploadAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fileRepo.Verify(r => r.AddAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
