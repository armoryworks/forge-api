using System.Security.Claims;

using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Eco;
using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Eco;

public class ImplementEcoRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 15, 30, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<IMediator> _mediator = new();
    private readonly ImplementEcoHandler _handler;

    public ImplementEcoRevisionTests()
    {
        var revisionHandler = new CreatePartRevisionHandler(_db, new PartRepository(_db, Mock.Of<IPartPricingResolver>()));
        var validator = new CreatePartRevisionCommandValidator();
        _mediator
            .Setup(m => m.Send(It.IsAny<CreatePartRevisionCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async (IRequest<PartRevisionResponseModel> request, CancellationToken ct) =>
            {
                var command = (CreatePartRevisionCommand)request;
                await validator.ValidateAndThrowAsync(command, ct);
                return await revisionHandler.Handle(command, ct);
            });

        var httpContext = new Mock<IHttpContextAccessor>();
        httpContext.Setup(h => h.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "test")),
        });

        _handler = new ImplementEcoHandler(_db, httpContext.Object, new FixedClock(Now), _mediator.Object);
    }

    private async Task<Part> SeedPartAsync(string partNumber, string revision)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            Revision = revision,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<EngineeringChangeOrder> SeedEcoAsync(params EcoAffectedItem[] items)
    {
        var eco = new EngineeringChangeOrder
        {
            EcoNumber = "ECO-0001",
            Title = "Thicker wall",
            Description = "Increase wall thickness",
            Status = EcoStatus.Approved,
            RequestedById = 7,
            AffectedItems = items.ToList(),
        };
        _db.EngineeringChangeOrders.Add(eco);
        await _db.SaveChangesAsync();
        return eco;
    }

    private static EcoAffectedItem Affects(string entityType, int entityId, string? newValue = null) => new()
    {
        EntityType = entityType,
        EntityId = entityId,
        ChangeDescription = "Change",
        NewValue = newValue,
    };

    [Fact]
    public async Task Revises_each_affected_part_once_and_logs_it()
    {
        var bracket = await SeedPartAsync("PRT-1", "A");
        var housing = await SeedPartAsync("PRT-2", "C");
        var eco = await SeedEcoAsync(
            Affects("Part", bracket.Id),
            Affects("Part", bracket.Id),
            Affects("Part", housing.Id),
            Affects("Drawing", bracket.Id));

        await _handler.Handle(new ImplementEcoCommand(eco.Id), CancellationToken.None);

        var revisions = await _db.PartRevisions.AsNoTracking().OrderBy(r => r.PartId).ToListAsync();
        revisions.Should().HaveCount(2);
        revisions[0].PartId.Should().Be(bracket.Id);
        revisions[0].Revision.Should().Be("B");
        revisions[1].PartId.Should().Be(housing.Id);
        revisions[1].Revision.Should().Be("D");
        revisions.Should().OnlyContain(r =>
            r.ChangeReason == "ECO ECO-0001: Thicker wall" && r.EffectiveDate == Now && r.IsCurrent);

        (await _db.Parts.AsNoTracking().SingleAsync(p => p.Id == bracket.Id)).Revision.Should().Be("B");
        (await _db.Parts.AsNoTracking().SingleAsync(p => p.Id == housing.Id)).Revision.Should().Be("D");

        var logs = await _db.ActivityLogs.AsNoTracking().Where(l => l.Action == "revised").ToListAsync();
        logs.Should().HaveCount(2);
        logs.Select(l => l.EntityId).Should().BeEquivalentTo([bracket.Id, housing.Id]);
        logs.Should().OnlyContain(l => l.EntityType == "Part");

        var implemented = await _db.EngineeringChangeOrders.AsNoTracking()
            .Include(e => e.AffectedItems).SingleAsync(e => e.Id == eco.Id);
        implemented.Status.Should().Be(EcoStatus.Implemented);
        implemented.ImplementedAt.Should().Be(Now);
        implemented.ImplementedById.Should().Be(7);
        implemented.AffectedItems.Should().OnlyContain(i => i.IsImplemented);
    }

    [Fact]
    public async Task Uses_the_revision_the_eco_names()
    {
        var part = await SeedPartAsync("PRT-1", "A");
        var eco = await SeedEcoAsync(Affects("Part", part.Id, "{\"revision\":\"C\"}"));

        await _handler.Handle(new ImplementEcoCommand(eco.Id), CancellationToken.None);

        (await _db.PartRevisions.AsNoTracking().SingleAsync()).Revision.Should().Be("C");
        (await _db.Parts.AsNoTracking().SingleAsync()).Revision.Should().Be("C");
    }

    [Fact]
    public async Task Skips_a_part_already_at_the_revision_the_eco_names()
    {
        var part = await SeedPartAsync("PRT-1", "C");
        var eco = await SeedEcoAsync(Affects("Part", part.Id, "{\"revision\":\"C\"}"));

        await _handler.Handle(new ImplementEcoCommand(eco.Id), CancellationToken.None);

        (await _db.PartRevisions.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await _db.ActivityLogs.AsNoTracking().AnyAsync(l => l.Action == "revised")).Should().BeFalse();
        (await _db.EngineeringChangeOrders.AsNoTracking().SingleAsync()).Status.Should().Be(EcoStatus.Implemented);
    }

    [Theory]
    [InlineData("Z", "AA")]
    [InlineData("AZ", "BA")]
    [InlineData("09", "10")]
    [InlineData("1", "2")]
    [InlineData("A9", "A10")]
    [InlineData("", "A")]
    public async Task Next_revision_code_follows_the_current_code(string current, string expected)
    {
        var part = await SeedPartAsync("PRT-1", current);
        var eco = await SeedEcoAsync(Affects("Part", part.Id));

        await _handler.Handle(new ImplementEcoCommand(eco.Id), CancellationToken.None);

        (await _db.PartRevisions.AsNoTracking().SingleAsync()).Revision.Should().Be(expected);
    }

    [Fact]
    public async Task Next_revision_code_skips_codes_already_used()
    {
        var part = await SeedPartAsync("PRT-1", "A");
        _db.PartRevisions.Add(new PartRevision { PartId = part.Id, Revision = "B", EffectiveDate = Now.AddDays(-30) });
        await _db.SaveChangesAsync();
        var eco = await SeedEcoAsync(Affects("Part", part.Id));

        await _handler.Handle(new ImplementEcoCommand(eco.Id), CancellationToken.None);

        (await _db.PartRevisions.AsNoTracking().SingleAsync(r => r.IsCurrent)).Revision.Should().Be("C");
    }

    [Theory]
    [InlineData("ZZZZZZZZZZ", null)]
    [InlineData("A", "{\"revision\":\"ABCDEFGHIJK\"}")]
    public async Task Rejects_a_revision_code_too_long_for_the_part_and_names_the_part(string current, string? newValue)
    {
        var part = await SeedPartAsync("PRT-LONG", current);
        var eco = await SeedEcoAsync(Affects("Part", part.Id, newValue));

        var act = () => _handler.Handle(new ImplementEcoCommand(eco.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PRT-LONG*");
        (await _db.PartRevisions.AsNoTracking().AnyAsync()).Should().BeFalse();
        (await _db.EngineeringChangeOrders.AsNoTracking().SingleAsync()).Status.Should().Be(EcoStatus.Approved);
    }

    [Theory]
    [InlineData("ABCDEFGHIJ", true)]
    [InlineData("ABCDEFGHIJK", false)]
    public void Revision_code_is_limited_to_the_part_revision_column(string revision, bool valid)
    {
        var result = new CreatePartRevisionCommandValidator()
            .Validate(new CreatePartRevisionCommand(1, revision, null, null, Now));

        result.IsValid.Should().Be(valid);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
