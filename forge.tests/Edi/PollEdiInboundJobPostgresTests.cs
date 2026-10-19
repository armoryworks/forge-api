using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Features.Edi;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Edi;

/// <summary>
/// The scheduled SFTP poll is the only inbound channel a polled partner has, and it runs with
/// no request user. A polled 850 must be processed the same way the manual receive endpoint
/// processes it (draft sales order + 997), not left in Received where no screen can recover it.
/// Runs on real Postgres so the save path the job actually uses is exercised.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PollEdiInboundJobPostgresTests(PostgresFixture fixture)
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 13, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class TokenTransport(string token, IReadOnlyList<string> inbox) : IEdiTransportService
    {
        public EdiTransportMethod Method => EdiTransportMethod.Sftp;
        public Task SendAsync(string payload, string connectionConfig, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> PollAsync(string connectionConfig, CancellationToken ct)
            => Task.FromResult(connectionConfig.Contains(token) ? inbox : (IReadOnlyList<string>)[]);
        public Task<bool> TestConnectionAsync(string connectionConfig, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class TransportFactory(IEdiTransportService transport) : IEdiTransportFactory
    {
        public IEdiTransportService For(EdiTransportMethod method) => transport;
    }

    private static string Order850(string poNumber) =>
        "ISA*00*          *00*          *ZZ*ACMEEDI        *ZZ*FORGE          *260613*0900*U*00401*000000042*0*P*>~\n"
        + "GS*PO*ACMEEDI*FORGE*20260613*0900*42*X*004010~\n"
        + "ST*850*0001~\n"
        + $"BEG*00*SA*{poNumber}**20260613~\n"
        + "PO1*1*500*EA*2.5*PE*BP*WIDGET-100~\n"
        + "PID*F****Molded widget, blue~\n"
        + "CTT*1~\n"
        + "SE*7*0001~\n"
        + "GE*1*42~\n"
        + "IEA*1*000000042~\n";

    private const string Inbound997 =
        "ISA*00*          *00*          *ZZ*ACMEEDI        *ZZ*FORGE          *260613*0900*U*00401*000000043*0*P*>~\n"
        + "GS*FA*ACMEEDI*FORGE*20260613*0900*43*X*004010~\n"
        + "ST*997*0001~\n"
        + "AK1*IN*7~\n"
        + "AK9*A*1*1*1~\n"
        + "SE*4*0001~\n"
        + "GE*1*43~\n"
        + "IEA*1*000000043~\n";

    private async Task<(int PartnerId, int CustomerId)> SeedPartnerAsync(string token)
    {
        await using var seed = fixture.CreateContext();
        var customer = new Customer { Name = $"EDI poll customer {token[..8]}" };
        seed.Set<Customer>().Add(customer);
        await seed.SaveChangesAsync();

        var partner = new EdiTradingPartner
        {
            Name = $"EDI poll partner {token[..8]}",
            CustomerId = customer.Id,
            QualifierId = "ZZ",
            QualifierValue = "ACMEEDI",
            InterchangeSenderId = "ACMEEDI",
            InterchangeReceiverId = "FORGE",
            ApplicationSenderId = "ACMEEDI",
            ApplicationReceiverId = "FORGE",
            DefaultFormat = EdiFormat.X12,
            TransportMethod = EdiTransportMethod.Sftp,
            TransportConfigJson = $"{{\"token\":\"{token}\"}}",
            AutoProcess = true,
            IsActive = true,
        };
        seed.EdiTradingPartners.Add(partner);
        await seed.SaveChangesAsync();
        return (partner.Id, customer.Id);
    }

    [Fact]
    public async Task Polled_850s_are_processed_into_draft_sales_orders()
    {
        var token = Guid.NewGuid().ToString("N");
        var poNumber = $"PO-{token[..10]}";
        var (partnerId, customerId) = await SeedPartnerAsync(token);

        await using (var db = fixture.CreateContext())
        {
            var transport = new TokenTransport(token, [Order850(poNumber), Order850(poNumber), Inbound997]);
            var service = new X12EdiService(
                db, new SalesOrderRepository(db), new TransportFactory(transport),
                new EdiPartNumberMapService(db), new FakeClock());
            var job = new PollEdiInboundJob(db, service, NullLogger<PollEdiInboundJob>.Instance);

            await job.PollAllPartnersAsync(CancellationToken.None);
        }

        await using var verify = fixture.CreateContext();
        var inbound = await verify.EdiTransactions.AsNoTracking()
            .Where(t => t.TradingPartnerId == partnerId && t.Direction == EdiDirection.Inbound)
            .OrderBy(t => t.Id)
            .ToListAsync();

        inbound.Should().HaveCount(3);
        inbound[0].Status.Should().Be(EdiTransactionStatus.Acknowledged);
        inbound[0].RelatedEntityType.Should().Be("SalesOrder");
        inbound[1].Status.Should().Be(EdiTransactionStatus.Error,
            "the same customer PO polled twice is a duplicate 850, not a second order");
        inbound[2].TransactionSet.Should().Be("997");
        inbound[2].Status.Should().Be(EdiTransactionStatus.Received,
            "only 850s are processed; an inbound 997 is stored, not marked as a failure");

        var orders = await verify.SalesOrders.AsNoTracking()
            .Where(s => s.CustomerId == customerId)
            .ToListAsync();
        orders.Should().ContainSingle();
        orders[0].Id.Should().Be(inbound[0].RelatedEntityId);
        orders[0].Status.Should().Be(SalesOrderStatus.Draft);
        orders[0].CustomerPO.Should().Be(poNumber);

        var acks = await verify.EdiTransactions.AsNoTracking()
            .CountAsync(t => t.TradingPartnerId == partnerId && t.Direction == EdiDirection.Outbound && t.TransactionSet == "997");
        acks.Should().Be(1);
    }
}
