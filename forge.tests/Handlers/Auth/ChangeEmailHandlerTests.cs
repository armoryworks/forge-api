using Bogus;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

public class ChangeEmailHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<ISessionStore> _sessionStoreMock;
    private readonly Mock<ISystemAuditWriter> _auditWriterMock;
    private readonly ChangeEmailHandler _handler;
    private readonly Faker _faker = new();

    public ChangeEmailHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        _sessionStoreMock = new Mock<ISessionStore>();
        _auditWriterMock = new Mock<ISystemAuditWriter>();

        _handler = new ChangeEmailHandler(_userManagerMock.Object, _sessionStoreMock.Object, _auditWriterMock.Object);
    }

    [Fact]
    public async Task Handle_ValidCurrentPassword_ChangesEmailRevokesSessionsAndAudits()
    {
        var user = new ApplicationUser
        {
            Id = 1,
            Email = "old@example.com",
            UserName = "old@example.com",
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
        };

        _userManagerMock.Setup(x => x.FindByIdAsync("1"))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "OldPassword1!"))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.FindByEmailAsync("new@example.com"))
            .ReturnsAsync((ApplicationUser?)null);
        _userManagerMock.Setup(x => x.SetEmailAsync(user, "new@example.com"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.SetUserNameAsync(user, "new@example.com"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        var command = new ChangeEmailCommand(1, "OldPassword1!", "new@example.com");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().NotThrowAsync();

        _userManagerMock.Verify(x => x.SetEmailAsync(user, "new@example.com"), Times.Once);
        _userManagerMock.Verify(x => x.SetUserNameAsync(user, "new@example.com"), Times.Once);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _auditWriterMock.Verify(x => x.WriteAsync("UserEmailChanged", 1, "ApplicationUser", 1,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        user.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_InvalidCurrentPassword_ThrowsUnauthorizedAccessException()
    {
        var user = new ApplicationUser
        {
            Id = 2,
            Email = "old@example.com",
            UserName = "old@example.com",
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
        };

        _userManagerMock.Setup(x => x.FindByIdAsync("2"))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "WrongPassword"))
            .ReturnsAsync(false);

        var command = new ChangeEmailCommand(2, "WrongPassword", "new@example.com");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Current password is incorrect");

        _userManagerMock.Verify(x => x.SetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailAlreadyInUse_ThrowsInvalidOperationException()
    {
        var user = new ApplicationUser
        {
            Id = 3,
            Email = "old@example.com",
            UserName = "old@example.com",
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
        };
        var other = new ApplicationUser
        {
            Id = 99,
            Email = "taken@example.com",
            UserName = "taken@example.com",
        };

        _userManagerMock.Setup(x => x.FindByIdAsync("3"))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "OldPassword1!"))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.FindByEmailAsync("taken@example.com"))
            .ReturnsAsync(other);

        var command = new ChangeEmailCommand(3, "OldPassword1!", "taken@example.com");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("That email address is already in use.");

        _userManagerMock.Verify(x => x.SetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameEmail_ThrowsInvalidOperationException()
    {
        var user = new ApplicationUser
        {
            Id = 4,
            Email = "same@example.com",
            UserName = "same@example.com",
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
        };

        _userManagerMock.Setup(x => x.FindByIdAsync("4"))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "OldPassword1!"))
            .ReturnsAsync(true);

        // Case-insensitive match against the current email.
        var command = new ChangeEmailCommand(4, "OldPassword1!", "SAME@example.com");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("That is already your email address.");

        _userManagerMock.Verify(x => x.SetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
