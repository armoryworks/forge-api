using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Edi;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Edi;

public sealed class EdiTradingPartnerCustomerTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();

    public void Dispose() => _db.Dispose();

    private async Task<(Customer Customer, EdiTradingPartner Partner)> SeedAsync()
    {
        var customer = new Customer { Name = "Buyer Co" };
        _db.Add(customer);
        await _db.SaveChangesAsync();
        var partner = new EdiTradingPartner { Name = "Buyer EDI", CustomerId = customer.Id, QualifierId = "ZZ", QualifierValue = "BUYER", IsActive = true };
        _db.Add(partner);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (customer, partner);
    }

    private Task Update(int id, UpdateEdiTradingPartnerRequestModel model)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetEdiTradingPartnerByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EdiTradingPartnerResponseModel());
        return new UpdateEdiTradingPartnerHandler(_db, mediator.Object, Mock.Of<IEdiCredentialProtector>())
            .Handle(new UpdateEdiTradingPartnerCommand(id, model), CancellationToken.None);
    }

    [Fact]
    public async Task Clear_customer_unlinks_the_partner_and_logs_it()
    {
        var (_, partner) = await SeedAsync();

        await Update(partner.Id, new UpdateEdiTradingPartnerRequestModel { ClearCustomer = true });

        (await _db.EdiTradingPartners.SingleAsync()).CustomerId.Should().BeNull();
        (await _db.ActivityLogs.Where(a => a.EntityType == "EdiTradingPartner").Select(a => a.Action).ToListAsync())
            .Should().Contain("updated");
    }

    [Fact]
    public async Task A_null_customer_without_clear_keeps_the_link()
    {
        var (customer, partner) = await SeedAsync();

        await Update(partner.Id, new UpdateEdiTradingPartnerRequestModel { Name = "Renamed" });

        var saved = await _db.EdiTradingPartners.SingleAsync();
        saved.CustomerId.Should().Be(customer.Id);
        saved.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Clear_customer_wins_over_a_customer_id_sent_with_it()
    {
        var (customer, partner) = await SeedAsync();

        await Update(partner.Id, new UpdateEdiTradingPartnerRequestModel { CustomerId = customer.Id, ClearCustomer = true });

        (await _db.EdiTradingPartners.SingleAsync()).CustomerId.Should().BeNull();
    }

    [Fact]
    public async Task Partners_filter_by_customer()
    {
        var (customer, _) = await SeedAsync();
        _db.Add(new EdiTradingPartner { Name = "Other", QualifierId = "ZZ", QualifierValue = "OTHER", IsActive = true });
        await _db.SaveChangesAsync();

        var result = await new GetEdiTradingPartnersHandler(_db)
            .Handle(new GetEdiTradingPartnersQuery(true, customer.Id), CancellationToken.None);

        result.Should().ContainSingle().Which.Name.Should().Be("Buyer EDI");
    }

    [Fact]
    public async Task Transactions_filter_by_the_document_they_were_sent_for()
    {
        var (_, partner) = await SeedAsync();
        _db.EdiTransactions.AddRange(
            new EdiTransaction { TradingPartnerId = partner.Id, Direction = EdiDirection.Outbound, TransactionSet = "856", RelatedEntityType = "Shipment", RelatedEntityId = 5, Status = EdiTransactionStatus.Acknowledged, ReceivedAt = DateTimeOffset.UnixEpoch },
            new EdiTransaction { TradingPartnerId = partner.Id, Direction = EdiDirection.Outbound, TransactionSet = "856", RelatedEntityType = "Shipment", RelatedEntityId = 6, ReceivedAt = DateTimeOffset.UnixEpoch },
            new EdiTransaction { TradingPartnerId = partner.Id, Direction = EdiDirection.Outbound, TransactionSet = "810", RelatedEntityType = "Invoice", RelatedEntityId = 5, ReceivedAt = DateTimeOffset.UnixEpoch });
        await _db.SaveChangesAsync();

        var result = await new GetEdiTransactionsHandler(_db).Handle(
            new GetEdiTransactionsQuery(null, "856", null, null, null, null, RelatedEntityType: "Shipment", RelatedEntityId: 5),
            CancellationToken.None);

        result.Data.Should().ContainSingle().Which.Status.Should().Be(EdiTransactionStatus.Acknowledged);
    }
}
