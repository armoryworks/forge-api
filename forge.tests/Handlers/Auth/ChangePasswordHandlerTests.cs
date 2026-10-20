using Bogus;
using FluentAssertions;
using ValidationException = FluentValidation.ValidationException;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Auth;

public class ChangePasswordHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<ISessionStore> _sessionStoreMock;
    private readonly Mock<ISystemAuditWriter> _auditWriterMock;
    private readonly ChangePasswordHandler _handler;
    private readonly Faker _faker = new();

    public ChangePasswordHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        _sessionStoreMock = new Mock<ISessionStore>();
        _auditWriterMock = new Mock<ISystemAuditWriter>();

        _handler = new ChangePasswordHandler(_userManagerMock.Object, _sessionStoreMock.Object, _auditWriterMock.Object);
    }

    [Fact]
    public async Task Handle_ValidCurrentPassword_ChangesPasswordAndRevokesAllSessions()
    {
        var user = new ApplicationUser
        {
            Id = 1,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
        };

        _userManagerMock.Setup(x => x.FindByIdAsync("1"))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "OldPassword1!"))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.ChangePasswordAsync(user, "OldPassword1!", "NewPassword1!"))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        var command = new ChangePasswordCommand(1, "OldPassword1!", "NewPassword1!");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().NotThrowAsync();

        _userManagerMock.Verify(x => x.ChangePasswordAsync(user, "OldPassword1!", "NewPassword1!"), Times.Once);
        _userManagerMock.Verify(x => x.UpdateAsync(user), Times.Once);
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        user.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_InvalidCurrentPassword_ThrowsValidationException()
    {
        var user = new ApplicationUser
        {
            Id = 2,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
        };

        _userManagerMock.Setup(x => x.FindByIdAsync("2"))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "WrongPassword"))
            .ReturnsAsync(false);

        var command = new ChangePasswordCommand(2, "WrongPassword", "NewPassword1!");

        var act = () => _handler.Handle(command, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e =>
            e.PropertyName == nameof(ChangePasswordCommand.CurrentPassword)
            && e.ErrorMessage == "Current password is incorrect.");

        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PolicyRejectsNewPassword_ThrowsValidationExceptionAndKeepsSessions()
    {
        var user = new ApplicationUser
        {
            Id = 3,
            Email = _faker.Internet.Email(),
            FirstName = _faker.Name.FirstName(),
            LastName = _faker.Name.LastName(),
        };

        _userManagerMock.Setup(x => x.FindByIdAsync("3"))
            .ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, "OldPassword1!"))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.ChangePasswordAsync(user, "OldPassword1!", "NEWPASSWORD1"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordRequiresLower", Description = "Passwords must have at least one lowercase ('a'-'z')." }));

        var command = new ChangePasswordCommand(3, "OldPassword1!", "NEWPASSWORD1");

        var act = () => _handler.Handle(command, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainSingle(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
        _sessionStoreMock.Verify(x => x.RevokeAllUserSessionsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Same1Password", "Same1Password", "New password must be different from the current password.")]
    [InlineData("OldPassword1!", "NOLOWER123", "New password must contain a lowercase letter.")]
    [InlineData("OldPassword1!", "noupper123", "New password must contain an uppercase letter.")]
    [InlineData("OldPassword1!", "NoDigitsHere", "New password must contain a digit.")]
    public void Validator_RejectsNewPasswordsThePolicyWouldRefuse(string current, string next, string message)
    {
        var result = new ChangePasswordValidator().Validate(new ChangePasswordCommand(1, current, next));

        result.Errors.Should().Contain(e => e.ErrorMessage == message);
    }

    [Fact]
    public void Validator_AcceptsCompliantNewPassword()
    {
        var result = new ChangePasswordValidator().Validate(new ChangePasswordCommand(1, "OldPassword1!", "NewPassword1"));

        result.IsValid.Should().BeTrue();
    }
}
