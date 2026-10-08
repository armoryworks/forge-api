using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.TrackTypes;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.TrackTypes;

public class UpdateTrackTypeHideStageTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdateTrackTypeHandler _handler;
    private TrackType _track = null!;

    public UpdateTrackTypeHideStageTests()
    {
        _handler = new UpdateTrackTypeHandler(new TrackTypeRepository(_db), _db);
    }

    private async Task SeedAsync()
    {
        _track = new TrackType { Name = "Production", Code = "production", SortOrder = 1 };
        _track.Stages.Add(new JobStage { Name = "Quote Requested", Code = "quote_requested", SortOrder = 1 });
        _track.Stages.Add(new JobStage { Name = "Quoted", Code = "quoted", SortOrder = 2 });
        _track.Stages.Add(new JobStage { Name = "Order Confirmed", Code = "order_confirmed", SortOrder = 3 });
        _track.Stages.Add(new JobStage { Name = "Shipped", Code = "shipped", SortOrder = 4, IsMandatory = true });
        _db.TrackTypes.Add(_track);
        await _db.SaveChangesAsync();
    }

    private int StageId(string code) => _track.Stages.Single(s => s.Code == code).Id;

    private async Task SeedJobAsync(string stageCode, bool archived = false)
    {
        _db.Jobs.Add(new Job
        {
            JobNumber = $"J-{Guid.NewGuid():N}"[..10],
            Title = "Bracket run",
            TrackTypeId = _track.Id,
            CurrentStageId = StageId(stageCode),
            IsArchived = archived,
        });
        await _db.SaveChangesAsync();
    }

    private UpdateTrackTypeCommand Command(params string[] hiddenCodes) =>
        new(_track.Id, "Production", "production", null,
            _track.Stages
                .OrderBy(s => s.SortOrder)
                .Select(s => new StageRequestModel(
                    s.Name, s.Code, s.SortOrder, s.Color, s.WIPLimit, s.IsIrreversible,
                    !hiddenCodes.Contains(s.Code)))
                .ToList());

    private async Task<List<JobStage>> StagesAsync() =>
        await _db.JobStages.AsNoTracking().Where(s => s.TrackTypeId == _track.Id).ToListAsync();

    [Fact]
    public async Task Hiding_a_status_that_holds_open_work_orders_is_refused()
    {
        await SeedAsync();
        await SeedJobAsync("quote_requested");
        await SeedJobAsync("quote_requested");
        await SeedJobAsync("quote_requested");

        var act = () => _handler.Handle(Command("quote_requested"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Move the 3 work orders in Quote Requested to another status first.");
        (await StagesAsync()).Should().OnlyContain(s => s.IsActive);
    }

    [Fact]
    public async Task Archived_work_orders_do_not_block_hiding()
    {
        await SeedAsync();
        await SeedJobAsync("quote_requested", archived: true);

        await _handler.Handle(Command("quote_requested"), CancellationToken.None);

        (await StagesAsync()).Single(s => s.Code == "quote_requested").IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Hiding_a_mandatory_status_is_refused()
    {
        await SeedAsync();

        var act = () => _handler.Handle(Command("shipped"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Shipped*required*");
    }

    [Fact]
    public async Task Hiding_every_status_is_refused()
    {
        await SeedAsync();
        _track.Stages.Single(s => s.Code == "shipped").IsMandatory = false;
        await _db.SaveChangesAsync();

        var act = () => _handler.Handle(
            Command("quote_requested", "quoted", "order_confirmed", "shipped"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*one status must stay visible*");
    }

    [Fact]
    public async Task Hide_then_show_round_trips_and_keeps_the_stage_id()
    {
        await SeedAsync();
        var quotedId = StageId("quoted");

        var afterHide = await _handler.Handle(Command("quote_requested", "quoted"), CancellationToken.None);

        afterHide.Stages.Select(s => s.Code).Should().Equal("order_confirmed", "shipped");
        (await StagesAsync()).Should().HaveCount(4);

        var afterShow = await _handler.Handle(Command(), CancellationToken.None);

        afterShow.Stages.Select(s => s.Code).Should().Equal("quote_requested", "quoted", "order_confirmed", "shipped");
        afterShow.Stages.Single(s => s.Code == "quoted").Id.Should().Be(quotedId);
    }

    [Fact]
    public async Task A_stage_left_out_of_the_request_is_hidden()
    {
        await SeedAsync();
        var command = Command();
        command = command with { Stages = command.Stages.Where(s => s.Code != "quoted").ToList() };

        await _handler.Handle(command, CancellationToken.None);

        (await StagesAsync()).Single(s => s.Code == "quoted").IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task One_activity_row_names_the_hidden_and_shown_statuses()
    {
        await SeedAsync();
        await _handler.Handle(Command("quote_requested", "quoted"), CancellationToken.None);
        await _handler.Handle(Command("quote_requested"), CancellationToken.None);

        var rows = await _db.ActivityLogs.AsNoTracking()
            .Where(a => a.EntityType == "TrackType" && a.EntityId == _track.Id
                && a.Action == "stage-visibility-changed")
            .OrderBy(a => a.Id)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].Description.Should().Be("Hid Quote Requested, Quoted.");
        rows[1].Description.Should().Be("Showed Quoted.");
    }
}
