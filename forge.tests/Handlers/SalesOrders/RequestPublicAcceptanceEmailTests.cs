using System.Text.Json;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.SalesOrders.Acceptance;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.SalesOrders;

public class RequestPublicAcceptanceEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 16, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = Mock.Of<IClock>(c => c.UtcNow == Now);
    private readonly IntegrationOutboxService _outbox;

    public RequestPublicAcceptanceEmailTests()
    {
        _outbox = new IntegrationOutboxService(_db, _clock, NullLogger<IntegrationOutboxService>.Instance);
    }

    private RequestPublicAcceptanceHandler Handler(string? frontendBaseUrl = "https://erp.example.com/")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FrontendBaseUrl"] = frontendBaseUrl })
            .Build();
        return new RequestPublicAcceptanceHandler(_db, _clock, _outbox, config, Mock.Of<IHttpContextAccessor>());
    }

    private async Task<int> SeedAsync()
    {
        var so = new SalesOrder { OrderNumber = "SO-00042", CustomerId = 1, Status = SalesOrderStatus.Draft };
        _db.Add(so);
        _db.SystemSettings.Add(new SystemSetting { Key = "company.name", Value = "Northwind Fabrication" });
        await _db.SaveChangesAsync();
        return so.Id;
    }

    private EmailMessage SingleEmail(out IntegrationOutboxEntry entry)
    {
        entry = _db.IntegrationOutboxEntries.Should().ContainSingle().Subject;
        return JsonSerializer.Deserialize<EmailMessage>(entry.Payload)!;
    }

    [Fact]
    public async Task The_acceptance_link_is_emailed_to_the_recipient()
    {
        var soId = await SeedAsync();

        var result = await Handler().Handle(
            new RequestPublicAcceptanceCommand(soId, " buyer@customer.example ", "PO-7712"), CancellationToken.None);

        var email = SingleEmail(out var entry);
        email.To.Should().Be("buyer@customer.example");
        email.Subject.Should().Be("Please accept order SO-00042 from Northwind Fabrication");
        email.HtmlBody.Should().Contain($"https://erp.example.com/accept/{result.Token}").And.Contain("10/22/2026");
        email.PlainTextBody.Should().Contain($"https://erp.example.com/accept/{result.Token}");
        entry.OperationKey.Should().Be($"so-acceptance-email:{result.Acceptance.Id}");
        entry.EntityType.Should().Be("SalesOrder");
        entry.EntityId.Should().Be(soId);
    }

    [Fact]
    public async Task A_spanish_recipient_gets_the_spanish_template()
    {
        var soId = await SeedAsync();

        await Handler().Handle(
            new RequestPublicAcceptanceCommand(soId, "comprador@cliente.example", "PO-7712", Language: "es"), CancellationToken.None);

        var email = SingleEmail(out _);
        email.Subject.Should().Be("Acepte su pedido SO-00042 de Northwind Fabrication");
        email.HtmlBody.Should().Contain("Revisar y aceptar el pedido").And.Contain("El enlace vence el 10/22/2026");
    }

    [Fact]
    public async Task Re_queueing_the_same_token_sends_one_email()
    {
        var soId = await SeedAsync();
        var result = await Handler().Handle(
            new RequestPublicAcceptanceCommand(soId, "buyer@customer.example", "PO-7712"), CancellationToken.None);
        var first = SingleEmail(out var entry);

        await _outbox.EnqueueEmailAsync(entry.OperationKey, first, "SalesOrder", soId);

        _db.IntegrationOutboxEntries.Should().ContainSingle();
        result.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Without_a_base_url_nothing_is_minted()
    {
        var soId = await SeedAsync();

        var act = () => Handler(frontendBaseUrl: null).Handle(
            new RequestPublicAcceptanceCommand(soId, "buyer@customer.example", "PO-7712"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Set FrontendBaseUrl*");
        _db.Attestations.Should().BeEmpty();
        _db.IntegrationOutboxEntries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("ES", true)]
    [InlineData(null, true)]
    [InlineData("fr", false)]
    public void Only_english_and_spanish_are_accepted(string? language, bool valid)
    {
        new RequestPublicAcceptanceValidator()
            .Validate(new RequestPublicAcceptanceCommand(1, "a@b.example", "PO-1", Language: language))
            .IsValid.Should().Be(valid);
    }
}
