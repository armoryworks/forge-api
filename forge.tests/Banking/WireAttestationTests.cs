using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using System.Security.Claims;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.VendorPayments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Banking;

/// <summary>
/// banking.wire.manual-attestation — wires sit Queued until a SECOND user attests the portal
/// entry (no fake auto-success on money movement). Proves: SoD (creator can't self-attest),
/// method/status guards, success flips to Succeeded with a MANUAL-WIRE reference and invokes
/// the shared settlement poster.
/// </summary>
public class WireAttestationTests
{
    private sealed class CapturingSettlement : ITransmissionSettlementService
    {
        public List<int> SettledTransmissionIds { get; } = [];
        public Task TryPostSettlementAsync(PaymentTransmission transmission, string paymentNumber, CancellationToken ct = default)
        { SettledTransmissionIds.Add(transmission.Id); return Task.CompletedTask; }
    }

    private static IHttpContextAccessor UserContext(int userId)
        => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
            },
        };

    private static async Task<(AppDbContext Db, VendorPayment Payment, PaymentTransmission Transmission)>
        SeedQueuedWireAsync(int creatorUserId = 7)
    {
        var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Pacific Tool Supply", IsActive = true };
        db.Set<Vendor>().Add(vendor);
        await db.SaveChangesAsync();
        var payment = new VendorPayment
        {
            PaymentNumber = "VPAY-WIRE-1", VendorId = vendor.Id,
            Method = PaymentMethod.Wire, Amount = 5000m, PaymentDate = DateTimeOffset.UtcNow,
        };
        db.VendorPayments.Add(payment);
        await db.SaveChangesAsync();
        var transmission = new PaymentTransmission
        {
            SourceType = "VendorPayment", SourceId = payment.Id,
            Status = PaymentTransmissionStatus.Queued, Amount = payment.Amount,
            Method = "Wire", CreatedByUserId = creatorUserId,
        };
        db.PaymentTransmissions.Add(transmission);
        await db.SaveChangesAsync();
        return (db, payment, transmission);
    }

    [Fact]
    public async Task Attest_SecondUser_Succeeds_AndPostsSettlement()
    {
        var (db, payment, transmission) = await SeedQueuedWireAsync(creatorUserId: 7);
        var settlement = new CapturingSettlement();
        var handler = new AttestWireTransmissionHandler(db, settlement, UserContext(9));

        await handler.Handle(new AttestWireTransmissionCommand(payment.Id, "FEDREF-123"), CancellationToken.None);

        var updated = await db.PaymentTransmissions.SingleAsync(t => t.Id == transmission.Id);
        updated.Status.Should().Be(PaymentTransmissionStatus.Succeeded);
        updated.SubmissionRef.Should().Be("MANUAL-WIRE/FEDREF-123");
        settlement.SettledTransmissionIds.Should().ContainSingle(id => id == transmission.Id);
    }

    [Fact]
    public async Task Attest_Creator_Blocked_SoD()
    {
        var (db, payment, _) = await SeedQueuedWireAsync(creatorUserId: 7);
        var handler = new AttestWireTransmissionHandler(db, new CapturingSettlement(), UserContext(7));

        var act = () => handler.Handle(new AttestWireTransmissionCommand(payment.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Segregation of duties*");
        (await db.PaymentTransmissions.SingleAsync()).Status.Should().Be(PaymentTransmissionStatus.Queued);
    }

    [Fact]
    public async Task Attest_NonWire_Blocked()
    {
        var (db, payment, _) = await SeedQueuedWireAsync();
        payment.Method = PaymentMethod.BankTransfer;
        await db.SaveChangesAsync();
        var handler = new AttestWireTransmissionHandler(db, new CapturingSettlement(), UserContext(9));

        var act = () => handler.Handle(new AttestWireTransmissionCommand(payment.Id, null), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Only wire payments*");
    }

    [Fact]
    public async Task Attest_AlreadySucceeded_Blocked()
    {
        var (db, payment, transmission) = await SeedQueuedWireAsync();
        transmission.Status = PaymentTransmissionStatus.Succeeded;
        await db.SaveChangesAsync();
        var handler = new AttestWireTransmissionHandler(db, new CapturingSettlement(), UserContext(9));

        var act = () => handler.Handle(new AttestWireTransmissionCommand(payment.Id, null), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only a Queued wire*");
    }
}
