using System.Text.Json;

using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class CreatePartNumberTests
{
    private const string DeletedPartMessage =
        "Part number 'OLD-100' belongs to a deleted part. Restore that part or choose another number.";

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly PartRepository _repo;
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBusinessIdentifierService> _identifiers = new();

    public CreatePartNumberTests()
    {
        _repo = new PartRepository(_db, Mock.Of<IPartPricingResolver>());
        _identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        _identifiers.Setup(i => i.RenameAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
    }

    private void AllowManualNumbers(bool allowed) =>
        _settings.Setup(s => s.FindByKeyAsync("parts.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "parts.allow_manual_numbers", Value = allowed ? "true" : "false" });

    private CreatePartHandler Handler() => new(
        _repo,
        _settings.Object,
        Mock.Of<ISyncQueueRepository>(),
        Mock.Of<IAccountingProviderFactory>(),
        Mock.Of<IBarcodeService>(),
        _identifiers.Object,
        _db,
        Mock.Of<ILogger<CreatePartHandler>>());

    private PartWorkflowAdapter Adapter() => new(_db, _repo, _settings.Object, _identifiers.Object);

    private static CreatePartCommand Command(string? partNumber) =>
        new("Bracket", null, null, ProcurementSource.Buy, InventoryClass.Component, null, partNumber);

    private async Task<Part> SeedPartAsync(string partNumber, bool deleted)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = "Existing",
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            DeletedAt = deleted ? new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero) : null,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private static string FailureOn(ValidationException ex, string field) =>
        ex.Errors.Should().ContainSingle(e => e.PropertyName == field).Which.ErrorMessage;

    [Fact]
    public async Task A_typed_number_is_rejected_when_manual_numbers_are_off()
    {
        AllowManualNumbers(false);

        var act = () => Handler().Handle(Command("ACME-1"), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        FailureOn(ex, "partNumber").Should().Be(
            "Manual part numbers are turned off. Leave Part Number blank to auto-number, or ask an admin to turn them on in Admin > Settings > Numbering.");
        (await _db.Parts.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_number_held_by_a_deleted_part_gets_the_deleted_part_message()
    {
        AllowManualNumbers(true);
        await SeedPartAsync("OLD-100", deleted: true);

        var act = () => Handler().Handle(Command("OLD-100"), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        FailureOn(ex, "partNumber").Should().Be(DeletedPartMessage);
    }

    [Fact]
    public async Task A_number_held_by_a_live_part_is_already_in_use()
    {
        AllowManualNumbers(true);
        await SeedPartAsync("LIVE-7", deleted: false);

        var act = () => Handler().Handle(Command("LIVE-7"), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        FailureOn(ex, "partNumber").Should().Be("Part number 'LIVE-7' is already in use.");
    }

    [Fact]
    public async Task Status_reports_none_active_and_deleted_and_honours_the_excluded_part()
    {
        var live = await SeedPartAsync("LIVE-7", deleted: false);
        await SeedPartAsync("OLD-100", deleted: true);

        (await _repo.PartNumberStatusAsync("FREE-1", null, CancellationToken.None)).Should().Be(PartNumberStatus.None);
        (await _repo.PartNumberStatusAsync("LIVE-7", null, CancellationToken.None)).Should().Be(PartNumberStatus.Active);
        (await _repo.PartNumberStatusAsync("OLD-100", null, CancellationToken.None)).Should().Be(PartNumberStatus.Deleted);
        (await _repo.PartNumberStatusAsync("LIVE-7", live.Id, CancellationToken.None)).Should().Be(PartNumberStatus.None);
    }

    [Fact]
    public async Task Workflow_create_rejects_a_typed_number_when_manual_numbers_are_off()
    {
        AllowManualNumbers(false);
        var payload = JsonDocument.Parse("""{ "name": "Bracket", "partNumber": "ACME-1" }""").RootElement;

        var act = () => Adapter().CreateDraftAsync(payload, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        FailureOn(ex, "partNumber").Should().StartWith("Manual part numbers are turned off.");
        (await _db.Parts.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Workflow_create_gives_the_deleted_part_message()
    {
        AllowManualNumbers(true);
        await SeedPartAsync("OLD-100", deleted: true);
        var payload = JsonDocument.Parse("""{ "name": "Bracket", "partNumber": "OLD-100" }""").RootElement;

        var act = () => Adapter().CreateDraftAsync(payload, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        FailureOn(ex, "partNumber").Should().Be(DeletedPartMessage);
    }

    [Fact]
    public async Task Workflow_rename_is_rejected_when_manual_numbers_are_off()
    {
        AllowManualNumbers(false);
        var part = await SeedPartAsync("PRT-00001", deleted: false);
        var fields = JsonDocument.Parse("""{ "partNumber": "ACME-42" }""").RootElement;

        var act = () => Adapter().ApplyAsync(part.Id, fields, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        FailureOn(ex, "partNumber").Should().Contain("Admin > Settings > Numbering");
        part.PartNumber.Should().Be("PRT-00001");
    }

    [Fact]
    public async Task Workflow_rename_to_a_deleted_parts_number_gives_the_deleted_part_message()
    {
        AllowManualNumbers(true);
        var part = await SeedPartAsync("PRT-00001", deleted: false);
        await SeedPartAsync("OLD-100", deleted: true);
        var fields = JsonDocument.Parse("""{ "partNumber": "OLD-100" }""").RootElement;

        var act = () => Adapter().ApplyAsync(part.Id, fields, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ValidationException>()).Which;
        FailureOn(ex, "partNumber").Should().Be(DeletedPartMessage);
        part.PartNumber.Should().Be("PRT-00001");
    }
}
