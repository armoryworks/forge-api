using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Moq;
using Forge.Api.Features.Admin;
using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Tests.Handlers.Auth;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Admin;

/// <summary>
/// One-shot kiosk identity provisioning. Covers the happy path, the
/// duplicate-barcode 409, and a live-shaped round trip proving the
/// provisioned credential passes KioskLoginHandler's check (the same mock
/// level KioskLoginHandlerTests uses).
/// </summary>
public class CreateKioskUserHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<RoleManager<IdentityRole<int>>> _roleManagerMock;
    private readonly AppDbContext _db;
    private readonly CreateKioskUserHandler _handler;
    private int _nextId = 1000;

    public CreateKioskUserHandlerTests()
    {
        _userManagerMock = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _roleManagerMock = new Mock<RoleManager<IdentityRole<int>>>(
            Mock.Of<IRoleStore<IdentityRole<int>>>(), null!, null!, null!, null!);
        _db = TestDbContextFactory.Create();

        // CreateAsync persists into the same in-memory context the handler
        // queries, so the uniqueness check + activity log see one store.
        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>()))
            .Callback<ApplicationUser>(u =>
            {
                u.Id = _nextId++;
                _db.Users.Add(u);
                _db.SaveChanges();
            })
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        _roleManagerMock.Setup(x => x.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        _roleManagerMock.Setup(x => x.RoleExistsAsync("ProductionWorker")).ReturnsAsync(true);

        _handler = new CreateKioskUserHandler(
            _userManagerMock.Object, _roleManagerMock.Object, _db);
    }

    private static CreateKioskUserCommand Command(
        string email = "kiosk-bot@example.com", string barcode = "KSK-0001", string pin = "4321")
        => new(email, "Kiosk", "Bot", "ProductionWorker", barcode, pin);

    [Fact]
    public async Task Handle_HappyPath_CreatesUserWithRoleBarcodeAndPin()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Id.Should().BeGreaterThan(0);
        result.Email.Should().Be("kiosk-bot@example.com");
        result.Role.Should().Be("ProductionWorker");
        result.Barcode.Should().Be("KSK-0001");

        var user = _db.Users.Single(u => u.Id == result.Id);
        user.EmployeeBarcode.Should().Be("KSK-0001");
        user.IsActive.Should().BeTrue();
        user.PinHash.Should().NotBeNullOrEmpty();
        // The stored hash must verify through the SAME PBKDF2 path kiosk-login uses.
        SetPinHandler.VerifyPin("4321", user.PinHash!).Should().BeTrue();
        SetPinHandler.VerifyPin("0000", user.PinHash!).Should().BeFalse();

        _userManagerMock.Verify(x => x.AddToRoleAsync(user, "ProductionWorker"), Times.Once);

        // House rule: mutating handler writes an ActivityLog row.
        _db.ActivityLogs.Should().Contain(a =>
            a.EntityType == "ApplicationUser" && a.EntityId == result.Id && a.Action == "created");
    }

    [Fact]
    public async Task Handle_DuplicateBarcode_ThrowsInvalidOperationException()
    {
        _db.Users.Add(new ApplicationUser
        {
            Id = 50,
            Email = "existing@example.com",
            UserName = "existing@example.com",
            FirstName = "Existing",
            LastName = "Worker",
            EmployeeBarcode = "KSK-0001",
            IsActive = true,
        });
        _db.SaveChanges();

        var act = () => _handler.Handle(Command(email: "second@example.com"), CancellationToken.None);

        // InvalidOperationException maps to 409 in ExceptionHandlingMiddleware.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*KSK-0001*already assigned*");
        _userManagerMock.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownRole_ThrowsInvalidOperationException()
    {
        var command = new CreateKioskUserCommand(
            "kiosk-bot@example.com", "Kiosk", "Bot", "NoSuchRole", "KSK-0002", "4321");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NoSuchRole*does not exist*");
        _userManagerMock.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Theory]
    [InlineData("123")]        // too short
    [InlineData("123456789")]  // too long
    [InlineData("12ab")]       // non-digits
    public void Validator_RejectsBadPins(string pin)
    {
        var validator = new CreateKioskUserValidator();
        var result = validator.Validate(Command(pin: pin));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_AcceptsFourToEightDigitPins()
    {
        var validator = new CreateKioskUserValidator();
        validator.Validate(Command(pin: "1234")).IsValid.Should().BeTrue();
        validator.Validate(Command(pin: "12345678")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CreatedIdentity_PassesKioskLoginCredentialCheck()
    {
        // Live-shaped round trip (same level KioskLoginHandlerTests exercises):
        // provision the identity, then run the real KioskLoginHandler over it
        // and prove the barcode+PIN pair authenticates.
        var created = await _handler.Handle(
            Command(barcode: "KSK-0042", pin: "8765"), CancellationToken.None);

        var loginUserManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        loginUserManager.Setup(x => x.Users)
            .Returns(new TestAsyncEnumerableQueryable<ApplicationUser>(_db.Users.ToList()));
        loginUserManager.Setup(x => x.IsLockedOutAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(false);
        loginUserManager.Setup(x => x.ResetAccessFailedCountAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        var tokenService = new Mock<ITokenService>();
        var tokenResult = new TokenResult("provisioned-jwt", "provisioned-jti", DateTimeOffset.UtcNow.AddHours(8));
        tokenService.Setup(x => x.GenerateToken(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<IList<string>>(),
                It.IsAny<TimeSpan?>(), It.IsAny<IDictionary<string, string>?>()))
            .Returns(tokenResult);

        var roleClaimsExpander = new Mock<IRoleClaimsExpander>();
        roleClaimsExpander
            .Setup(x => x.GetEffectiveRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ProductionWorker" });

        var loginHandler = new KioskLoginHandler(
            loginUserManager.Object, tokenService.Object,
            Mock.Of<ISessionStore>(), Mock.Of<IHttpContextAccessor>(),
            roleClaimsExpander.Object);

        // Correct PIN → authenticated.
        var login = await loginHandler.Handle(
            new KioskLoginCommand("KSK-0042", "8765"), CancellationToken.None);
        login.Token.Should().Be("provisioned-jwt");
        login.User.Id.Should().Be(created.Id);

        // Wrong PIN with the same barcode → rejected (credential is real, not a stub).
        var badPin = () => loginHandler.Handle(
            new KioskLoginCommand("KSK-0042", "1111"), CancellationToken.None);
        await badPin.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Invalid barcode or PIN");
    }
}
