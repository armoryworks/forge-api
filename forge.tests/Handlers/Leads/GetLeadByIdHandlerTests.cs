using FluentAssertions;

using Forge.Api.Features.Leads;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Leads;

public class GetLeadByIdHandlerTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetLeadByIdHandler _handler;

    public GetLeadByIdHandlerTests()
    {
        _handler = new GetLeadByIdHandler(new LeadRepository(_db));
    }

    [Fact]
    public async Task Handle_ReturnsStoredClassificationOutreachAndOwnershipFields()
    {
        var signedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var expiresAt = signedAt.AddYears(2);
        _db.Leads.Add(new Lead
        {
            Id = 3,
            CompanyName = "Acme Corp",
            CreatedBy = 1,
            CampaignId = 7,
            OutreachState = OutreachState.CallbackScheduled,
            IcpScore = 82,
            AssignedToUserId = 4,
            AccountId = 9,
            CapabilityFit = CapabilityFitStatus.Fits,
            NdaState = NdaState.InForce,
            NdaSignedAt = signedAt,
            NdaExpiresAt = expiresAt,
            ExportControl = ExportControlClearance.Pending,
            SecondaryOwnerUserId = 5,
            PartClassCode = "MACH-AL",
        });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new GetLeadByIdQuery(3), CancellationToken.None);

        result.Should().NotBeNull();
        result!.CampaignId.Should().Be(7);
        result.OutreachState.Should().Be(OutreachState.CallbackScheduled);
        result.IcpScore.Should().Be(82);
        result.AssignedToUserId.Should().Be(4);
        result.AccountId.Should().Be(9);
        result.CapabilityFit.Should().Be(CapabilityFitStatus.Fits);
        result.NdaState.Should().Be(NdaState.InForce);
        result.NdaSignedAt.Should().Be(signedAt);
        result.NdaExpiresAt.Should().Be(expiresAt);
        result.ExportControl.Should().Be(ExportControlClearance.Pending);
        result.SecondaryOwnerUserId.Should().Be(5);
        result.PartClassCode.Should().Be("MACH-AL");
    }
}
