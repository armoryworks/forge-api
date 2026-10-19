using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Admin;
using Forge.Core.Interfaces;
using Forge.Core.Settings;

namespace Forge.Tests.Handlers.Admin;

/// <summary>
/// The admin Test button sends the catalog's provider key. Every catalog
/// provider must reach a real test or an explicit "no test yet" answer,
/// never the "Unknown integration provider" fallback.
/// </summary>
public class TestIntegrationConnectionHandlerTests
{
    private readonly Mock<IAiService> _ai = new();
    private readonly Mock<IAccountingProviderFactory> _accounting = new();

    private TestIntegrationConnectionHandler MakeHandler() => new(
        Mock.Of<IEmailService>(),
        Mock.Of<IStorageService>(),
        Mock.Of<IAddressValidationService>(),
        Mock.Of<IDocumentSigningService>(),
        _ai.Object,
        [],
        _accounting.Object,
        NullLogger<TestIntegrationConnectionHandler>.Instance);

    public static TheoryData<string> CatalogProviders()
    {
        var data = new TheoryData<string>();
        foreach (var d in IntegrationDescriptorCatalog.All)
            data.Add(d.Provider);
        return data;
    }

    [Theory]
    [MemberData(nameof(CatalogProviders))]
    public async Task Handle_EveryCatalogProvider_IsKnown(string provider)
    {
        var result = await MakeHandler().Handle(new TestIntegrationConnectionCommand(provider), default);

        result.Message.Should().NotStartWith("Unknown integration provider");
    }

    [Fact]
    public async Task Handle_AiProvider_ChecksTheAiService()
    {
        _ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await MakeHandler().Handle(new TestIntegrationConnectionCommand("ai"), default);

        result.Success.Should().BeTrue();
        _ai.Verify(a => a.IsAvailableAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_QuickBooks_UsesTheAccountingProviderTest()
    {
        var qb = new Mock<IAccountingService>();
        qb.SetupGet(p => p.ProviderName).Returns("QuickBooks Online");
        qb.Setup(p => p.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _accounting.Setup(f => f.GetProvider("quickbooks")).Returns(qb.Object);

        var result = await MakeHandler().Handle(new TestIntegrationConnectionCommand("quickbooks"), default);

        result.Success.Should().BeTrue();
        qb.Verify(p => p.TestConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("gdrive")]
    [InlineData("twilio")]
    [InlineData("gmail-oauth")]
    [InlineData("microsoft-oauth")]
    public async Task Handle_ProviderWithoutATest_SaysSoInsteadOfSucceeding(string provider)
    {
        var result = await MakeHandler().Handle(new TestIntegrationConnectionCommand(provider), default);

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("No connection test is available");
    }
}
