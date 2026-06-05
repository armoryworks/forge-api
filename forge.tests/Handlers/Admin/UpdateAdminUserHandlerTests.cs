using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Admin;
using Forge.Api.Services;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

public class UpdateAdminUserHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<ISystemAuditWriter> _auditWriterMock;
    private readonly AppDbContext _db;
    private readonly UpdateAdminUserHandler _handler;

    public UpdateAdminUserHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _auditWriterMock = new Mock<ISystemAuditWriter>();
        _db = TestDbContextFactory.Create();

        _userManagerMock.Setup(x => x.NormalizeEmail(It.IsAny<string>()))
            .Returns((string? e) => e?.ToUpperInvariant() ?? string.Empty);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string>());
        _userManagerMock.Setup(x => x.HasPasswordAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(true);

        _handler = new UpdateAdminUserHandler(_userManagerMock.Object, _db, _auditWriterMock.Object);
    }

    private ApplicationUser SeedUser(int id, string email)
    {
        var user = new ApplicationUser
        {
            Id = id,
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            FirstName = "First",
            LastName = "Last",
            Initials = "FL",
            AvatarColor = "#0d9488",
            IsActive = true,
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task Handle_EmailChanged_SyncsEmailAndUserNameAndWritesAudit()
    {
        var user = SeedUser(1, "typo@example.com");

        var result = await _handler.Handle(
            new UpdateAdminUserCommand(1, null, null, null, null, null, null, Email: "correct@example.com"),
            CancellationToken.None);

        result.Email.Should().Be("correct@example.com");
        user.Email.Should().Be("correct@example.com");
        // Email is the login identity — UserName must follow it.
        user.UserName.Should().Be("correct@example.com");
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
        _auditWriterMock.Verify(x => x.WriteAsync(
            "UserEmailChanged", It.IsAny<int>(), "ApplicationUser", 1,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmailUnchanged_DoesNotWriteEmailAudit()
    {
        SeedUser(1, "same@example.com");

        await _handler.Handle(
            new UpdateAdminUserCommand(1, "NewFirst", null, null, null, null, null, Email: "same@example.com"),
            CancellationToken.None);

        _auditWriterMock.Verify(x => x.WriteAsync(
            "UserEmailChanged", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailAlreadyInUse_ThrowsInvalidOperationException()
    {
        SeedUser(1, "first@example.com");
        SeedUser(2, "taken@example.com");

        var act = () => _handler.Handle(
            new UpdateAdminUserCommand(1, null, null, null, null, null, null, Email: "taken@example.com"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already in use*");
        _userManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }
}
