using System.Text.RegularExpressions;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Api.Features.Auth;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Tests.Handlers.Admin;

public class RegeneratedSetupTokenTests
{
    private readonly ApplicationUser _user = new()
    {
        Id = 42,
        Email = "pending@example.com",
        FirstName = "Pending",
        LastName = "Person",
        SetupToken = CreateAdminUserHandler.HashSetupToken("OLDC-ODE2"),
        SetupTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
    };

    private readonly Mock<UserManager<ApplicationUser>> _userManager;

    public RegeneratedSetupTokenTests()
    {
        _userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _userManager.Setup(x => x.FindByIdAsync("42")).ReturnsAsync(_user);
        _userManager.Setup(x => x.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(x => x.Users).Returns(() => new List<ApplicationUser> { _user }.AsQueryable());
    }

    [Fact]
    public async Task GenerateSetupToken_StoresTheHash_AndTheReturnedCodeValidates()
    {
        var issued = await new GenerateSetupTokenHandler(_userManager.Object)
            .Handle(new GenerateSetupTokenCommand(42), CancellationToken.None);

        issued.Token.Should().MatchRegex("^[A-Z2-9]{4}-[A-Z2-9]{4}$");
        _user.SetupToken.Should().NotBe(issued.Token);
        _user.SetupToken.Should().Be(CreateAdminUserHandler.HashSetupToken(issued.Token));

        var info = await new ValidateSetupTokenHandler(_userManager.Object)
            .Handle(new ValidateSetupTokenQuery(issued.Token), CancellationToken.None);
        info.Email.Should().Be("pending@example.com");
    }

    [Fact]
    public async Task GenerateSetupToken_InvalidatesThePreviousCode()
    {
        await new GenerateSetupTokenHandler(_userManager.Object)
            .Handle(new GenerateSetupTokenCommand(42), CancellationToken.None);

        var act = () => new ValidateSetupTokenHandler(_userManager.Object)
            .Handle(new ValidateSetupTokenQuery("OLDC-ODE2"), CancellationToken.None);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SendSetupInvite_EmailsAPlaintextCodeThatValidates_NeverTheStoredHash()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GenerateSetupTokenCommand>(), It.IsAny<CancellationToken>()))
            .Returns((GenerateSetupTokenCommand c, CancellationToken ct) =>
                new GenerateSetupTokenHandler(_userManager.Object).Handle(c, ct));
        var settings = new Mock<ISystemSettingRepository>();
        EmailMessage? sent = null;
        var outbox = new Mock<IIntegrationOutboxService>();
        outbox.Setup(o => o.EnqueueEmailAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, EmailMessage m, string? _, int? _, CancellationToken _) => sent = m)
            .ReturnsAsync(new IntegrationOutboxEntry());

        await new SendSetupInviteHandler(_userManager.Object, mediator.Object, outbox.Object, settings.Object)
            .Handle(new SendSetupInviteCommand(42, "https://forge.example"), CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.HtmlBody.Should().NotContain(_user.SetupToken!);
        sent.HtmlBody.Should().NotContain("Manage preferences");
        var code = Regex.Match(sent.HtmlBody, "https://forge\\.example/setup/([A-Z2-9]{4}-[A-Z2-9]{4})").Groups[1].Value;
        code.Should().NotBeEmpty();

        var info = await new ValidateSetupTokenHandler(_userManager.Object)
            .Handle(new ValidateSetupTokenQuery(code), CancellationToken.None);
        info.Email.Should().Be("pending@example.com");
    }

    [Fact]
    public async Task SendSetupInvite_OperationKey_CarriesNothingDerivedFromTheToken()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GenerateSetupTokenCommand>(), It.IsAny<CancellationToken>()))
            .Returns((GenerateSetupTokenCommand c, CancellationToken ct) =>
                new GenerateSetupTokenHandler(_userManager.Object).Handle(c, ct));
        var settings = new Mock<ISystemSettingRepository>();
        string? operationKey = null;
        EmailMessage? sent = null;
        var outbox = new Mock<IIntegrationOutboxService>();
        outbox.Setup(o => o.EnqueueEmailAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((string key, EmailMessage m, string? _, int? _, CancellationToken _) => { operationKey = key; sent = m; })
            .ReturnsAsync(new IntegrationOutboxEntry());

        await new SendSetupInviteHandler(_userManager.Object, mediator.Object, outbox.Object, settings.Object)
            .Handle(new SendSetupInviteCommand(42, "https://forge.example"), CancellationToken.None);

        var code = Regex.Match(sent!.HtmlBody, "https://forge\\.example/setup/([A-Z2-9]{4}-[A-Z2-9]{4})").Groups[1].Value;
        var hash = CreateAdminUserHandler.HashSetupToken(code);

        operationKey.Should().StartWith("setup-invite:42:");
        operationKey.Should().NotContain(code);
        operationKey.Should().NotContain(hash);
        operationKey.Should().NotContainEquivalentOf(hash[..16]);
    }
}
