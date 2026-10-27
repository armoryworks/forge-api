using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Features.Cpq;
using Forge.Api.Features.Parts;
using Forge.Api.Services;
using Forge.Api.Services.Cpq;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Services.Cpq;

/// <summary>
/// A configured part: the base part duplicated (BOM and routing) through the normal duplicate path, under a number
/// derived from the base part and the configuration, plus the components and steps the chosen options add.
/// </summary>
public sealed class BuiltInCpqPartTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly IClock _clock = new SystemClock();

    private BuiltInCpqService Service(Mock<IBomRevisionService> revisions)
    {
        var sender = new Mock<IMediator>();
        var accounting = new Mock<IAccountingProviderFactory>();
        accounting.Setup(a => a.GetActiveProviderAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IAccountingService?)null);
        sender.Setup(s => s.Send(It.IsAny<RecalculatePartStandardCostCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecalculatedStandardCostModel)null!);
        sender.Setup(s => s.Send(It.IsAny<ClonePartCommand>(), It.IsAny<CancellationToken>()))
            .Returns((ClonePartCommand c, CancellationToken ct) => new ClonePartHandler(
                _db, new PartRepository(_db, new PartPricingResolver(_db)), new SystemSettingRepository(_db),
                new BarcodeService(_db, new HttpContextAccessor()), new BusinessIdentifierService(_db, _clock), revisions.Object,
                Mock.Of<ISyncQueueRepository>(), accounting.Object, sender.Object, _clock, NullLogger<ClonePartHandler>.Instance)
                .Handle(c, ct));
        return new BuiltInCpqService(_db, sender.Object, _clock, new SystemSettingRepository(_db), revisions.Object,
            new TaxOverrideGuard(_db, sender.Object, _clock));
    }

    private async Task<int> SeedAsync()
    {
        var basePart = new Part { PartNumber = "BRK-100", Name = "Bracket", InventoryClass = InventoryClass.FinishedGood, ProcurementSource = ProcurementSource.Make, Status = PartStatus.Active };
        var bolt = new Part { PartNumber = "BOLT-8", Name = "Bolt", InventoryClass = InventoryClass.Component };
        var gold = new Part { PartNumber = "PLT-GOLD", Name = "Gold plate", InventoryClass = InventoryClass.Component };
        _db.Parts.AddRange(basePart, bolt, gold);
        await _db.SaveChangesAsync();
        _db.BOMLines.Add(new BOMLine { ParentPartId = basePart.Id, ChildPartId = bolt.Id, Quantity = 4m, SortOrder = 10 });
        _db.Operations.Add(new Operation { PartId = basePart.Id, StepNumber = 10, Title = "Cut" });

        var configurator = new ProductConfigurator { Name = "Bracket builder", BasePartId = basePart.Id, BasePrice = 50m };
        configurator.Options.Add(new ConfiguratorOption
        {
            Name = "Finish", OptionType = ConfiguratorOptionType.Select, IsRequired = true, ValuesJson = """["Raw", "Gold"]""",
            BomImpactJson = """[{"when": "Gold", "partNumber": "PLT-GOLD", "quantity": 1}, {"when": "Gold", "partNumber": "BOLT-8", "quantity": 2}]""",
            RoutingImpactJson = """[{"when": "Gold", "title": "Plate", "setupMinutes": 15}]""",
        });
        _db.ProductConfigurators.Add(configurator);
        await _db.SaveChangesAsync();
        var configuration = new ProductConfiguration
        {
            ConfiguratorId = configurator.Id, ConfigurationCode = "CFG-1", SelectionsJson = """{"Finish":"Gold"}""", ComputedPrice = 50m,
        };
        _db.ProductConfigurations.Add(configuration);
        await _db.SaveChangesAsync();
        return configuration.Id;
    }

    [Fact]
    public async Task Generating_a_part_copies_the_base_and_adds_the_option_components_and_steps()
    {
        var configurationId = await SeedAsync();
        var revisions = new Mock<IBomRevisionService>();

        var partId = await new GeneratePartFromConfigHandler(_db, Service(revisions)).Handle(
            new GeneratePartFromConfigCommand(configurationId), CancellationToken.None);

        var part = await _db.Parts.SingleAsync(p => p.Id == partId);
        part.PartNumber.Should().Be($"BRK-100-C{configurationId}");
        part.Description.Should().Be("Finish: Gold");
        var bom = await _db.BOMLines.Where(b => b.ParentPartId == partId).Include(b => b.ChildPart).ToListAsync();
        bom.Should().HaveCount(2);
        bom.Single(b => b.ChildPart.PartNumber == "BOLT-8").Quantity.Should().Be(6m);
        bom.Single(b => b.ChildPart.PartNumber == "PLT-GOLD").Quantity.Should().Be(1m);
        var steps = await _db.Operations.Where(o => o.PartId == partId).OrderBy(o => o.StepNumber).Select(o => o.Title).ToListAsync();
        steps.Should().Equal("Cut", "Plate");
        (await _db.ProductConfigurations.SingleAsync()).PartId.Should().Be(partId);
        revisions.Verify(r => r.CaptureCurrentStateAsync(partId, It.IsAny<int?>(), "Configured by CFG-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_component_part_that_no_longer_exists_stops_the_part_with_a_reason()
    {
        var configurationId = await SeedAsync();
        var gold = await _db.Parts.SingleAsync(p => p.PartNumber == "PLT-GOLD");
        gold.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var act = () => Service(new Mock<IBomRevisionService>()).GeneratePartFromConfigurationAsync(configurationId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PLT-GOLD*does not exist*");
        (await _db.Parts.CountAsync()).Should().Be(2);
    }
}
