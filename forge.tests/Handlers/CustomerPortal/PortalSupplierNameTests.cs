using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Api.Features.CustomerPortal;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.CustomerPortal;

public class PortalSupplierNameTests : IDisposable
{
    private const string ContactEmail = "buyer@customer.test";

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IPortalAuthService> _portalAuth = new();
    private EmailMessage? _sent;

    public PortalSupplierNameTests()
    {
        _portalAuth.Setup(p => p.GenerateMagicLinkToken()).Returns(("plain-token", "token-hash"));
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => _sent = m)
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync(string? companyName)
    {
        if (companyName is not null)
            _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = companyName });
        var customer = new Customer { Name = "Buyer Co" };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        _db.Contacts.Add(new Contact { CustomerId = customer.Id, FirstName = "Pat", LastName = "Buyer", Email = ContactEmail });
        await _db.SaveChangesAsync();
    }

    private Task<RequestMagicLinkResult> RequestAsync() =>
        new RequestMagicLinkHandler(
                _db, _portalAuth.Object, _email.Object,
                new ConfigurationBuilder().Build(),
                Mock.Of<IHostEnvironment>(),
                Mock.Of<IClock>(c => c.UtcNow == DateTimeOffset.UtcNow),
                NullLogger<RequestMagicLinkHandler>.Instance)
            .Handle(new RequestMagicLinkCommand(ContactEmail, "https://forge.test"), CancellationToken.None);

    [Fact]
    public async Task MagicLinkEmail_NamesTheSupplier_WhenCompanyNameIsSet()
    {
        await SeedAsync("Harbor Gear Works");

        await RequestAsync();

        _sent.Should().NotBeNull();
        _sent!.Subject.Should().Be("Sign in to the Harbor Gear Works customer portal");
        _sent.PlainTextBody.Should().Contain("Sign in to the Harbor Gear Works customer portal");
        _sent.HtmlBody.Should().Contain("sign in to the Harbor Gear Works customer portal");
    }

    [Fact]
    public async Task MagicLinkEmail_HtmlEncodesTheSupplierName()
    {
        await SeedAsync("Smith & <Sons>");

        await RequestAsync();

        _sent!.HtmlBody.Should().Contain("the Smith &amp; &lt;Sons&gt; customer portal");
        _sent.Subject.Should().Be("Sign in to the Smith & <Sons> customer portal");
    }

    [Fact]
    public async Task MagicLinkEmail_KeepsGenericWording_WhenNoCompanyName()
    {
        await SeedAsync(null);

        await RequestAsync();

        _sent!.Subject.Should().Be("Sign in to your customer portal");
        _sent.PlainTextBody.Should().Contain("Sign in to the customer portal");
    }

    [Fact]
    public async Task BrandSettings_IncludeCompanyName()
    {
        await SeedAsync("Harbor Gear Works");

        var brand = await new GetBrandSettingsHandler(new SystemSettingRepository(_db))
            .Handle(new GetBrandSettingsQuery(), CancellationToken.None);

        brand.CompanyName.Should().Be("Harbor Gear Works");
    }

    [Fact]
    public async Task BrandSettings_CompanyNameIsNull_WhenUnset()
    {
        await SeedAsync(null);

        var brand = await new GetBrandSettingsHandler(new SystemSettingRepository(_db))
            .Handle(new GetBrandSettingsQuery(), CancellationToken.None);

        brand.CompanyName.Should().BeNull();
    }
}
