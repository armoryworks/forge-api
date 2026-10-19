using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Capabilities.BulkToggle;
using Forge.Api.Features.Capabilities.Descriptor;
using Forge.Api.Features.Capabilities.Validate;
using Forge.Api.Features.Discovery.Apply;
using Forge.Api.Features.Discovery.Preview;
using Forge.Api.Features.Presets.Apply;
using Forge.Api.Features.Presets.Custom;
using Forge.Api.Features.Presets.Detail;
using Forge.Api.Features.Presets.Models;
using Forge.Api.Features.Presets.Preview;
using Forge.Api.Features.Terminology;
using Forge.Api.Hubs;
using Forge.Api.Services;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Capabilities;

/// <summary>
/// The general-ledger rules (opening-balances gate, live-ledger guard, GL-VIEW cascade) hold on
/// every path that changes capability state, not only the single toggle: bulk toggle, preset
/// apply, Custom apply, discovery apply and their previews. Presets do not own the ledger
/// capabilities, so applying one on a live ledger keeps the ledger on, and a preset that would
/// switch accounting to the external system is refused before anything is written.
/// </summary>
public class GlCapabilityGuardPathTests
{
    private const int BookId = 1;
    private const string FullGl = "CAP-ACCT-FULLGL";
    private const string GlView = "CAP-ACCT-GL-VIEW";
    private const string Builtin = "CAP-ACCT-BUILTIN";
    private const string External = "CAP-ACCT-EXTERNAL";
    private const string Period = "CAP-ACCT-PERIOD";

    private sealed class DbSnapshot(AppDbContext db) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current => new(
            db.Capabilities.AsNoTracking().ToDictionary(c => c.Code, c => c.Enabled, StringComparer.Ordinal),
            DateTimeOffset.UtcNow);

        public bool IsEnabled(string code) => Current.IsEnabled(code);

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static AppDbContext Install(bool openingJournal, params string[] alsoOn)
    {
        var db = TestDbContextFactory.Create();
        var on = new HashSet<string>(CapabilityCatalog.All.Where(c => c.IsDefaultOn).Select(c => c.Code), StringComparer.Ordinal);
        on.UnionWith(alsoOn);
        foreach (var def in CapabilityCatalog.All)
        {
            db.Capabilities.Add(new Capability
            {
                Code = def.Code,
                Area = def.Area,
                Name = def.Name,
                Description = def.Description,
                IsDefaultOn = def.IsDefaultOn,
                Enabled = on.Contains(def.Code),
            });
        }
        db.Books.Add(new Book
        {
            Id = BookId,
            Code = "MAIN",
            Name = "Default Book",
            FunctionalCurrencyId = 1,
            ReportingTimeZone = "America/Denver",
            IsActive = true,
        });
        if (openingJournal)
        {
            db.JournalEntries.Add(new JournalEntry
            {
                Id = 1,
                BookId = BookId,
                EntryNumber = 1,
                EntryDate = new DateOnly(2026, 1, 1),
                FiscalPeriodId = 1,
                FiscalYearId = 1,
                Source = JournalSource.Conversion,
                CurrencyId = 1,
                Status = JournalEntryStatus.Posted,
                Memo = "Opening balances",
            });
        }
        db.SaveChanges();
        return db;
    }

    private static AppDbContext LiveLedger() => Install(openingJournal: true, FullGl, GlView, Period);

    private static bool IsOn(AppDbContext db, string code) =>
        db.Capabilities.AsNoTracking().Single(c => c.Code == code).Enabled;

    private static IFullGlCapabilityGuard Guard(AppDbContext db) => new FullGlCapabilityGuard(db, new GlCapabilityGate(db));

    private static IHubContext<NotificationHub> Hub()
    {
        var proxy = new Mock<IClientProxy>();
        proxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.SetupGet(c => c.All).Returns(proxy.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        return hub.Object;
    }

    private static ISystemAuditWriter Audit()
    {
        var audit = new Mock<ISystemAuditWriter>();
        audit
            .Setup(a => a.WriteAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return audit.Object;
    }

    private static BulkToggleCapabilitiesHandler Bulk(AppDbContext db) =>
        new(db, new DbSnapshot(db), Audit(), Hub(), Guard(db));

    private static IMediator Mediator(AppDbContext db)
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<BulkToggleCapabilitiesCommand>(), It.IsAny<CancellationToken>()))
            .Returns((BulkToggleCapabilitiesCommand c, CancellationToken t) => Bulk(db).Handle(c, t));
        mediator
            .Setup(m => m.Send(It.IsAny<ValidateCapabilityChangesCommand>(), It.IsAny<CancellationToken>()))
            .Returns((ValidateCapabilityChangesCommand c, CancellationToken t) =>
                new ValidateCapabilityChangesHandler(db, new DbSnapshot(db), Guard(db)).Handle(c, t));
        return mediator.Object;
    }

    private static ApplyPresetHandler ApplyPreset(AppDbContext db) =>
        new(db, new DbSnapshot(db), Mediator(db), Audit(),
            new TerminologySpanishTranslator(db, Mock.Of<II18nTranslationService>()), Hub(), Guard(db));

    private static IEnumerable<string> ViolationCodes(CapabilityMutationException ex) =>
        ((IEnumerable<object>)ex.Extra["violations"]!)
            .Select(v => (string)v.GetType().GetProperty("code")!.GetValue(v)!);

    [Fact]
    public async Task Bulk_enabling_BUILTIN_and_FULLGL_together_is_refused_without_opening_balances()
    {
        await using var db = Install(openingJournal: false);
        db.Capabilities.Single(c => c.Code == Builtin).Enabled = false;
        db.SaveChanges();

        var act = () => Bulk(db).Handle(new BulkToggleCapabilitiesCommand(
            [new BulkToggleItem(Builtin, true, null), new BulkToggleItem(FullGl, true, null)], null), default);

        var ex = await act.Should().ThrowAsync<CapabilityMutationException>();
        ex.Which.ErrorCode.Should().Be("bulk-validation-failed");
        ViolationCodes(ex.Which).Should().Equal("capability-gl-opening-balances");
        IsOn(db, FullGl).Should().BeFalse();
        IsOn(db, Builtin).Should().BeFalse();
    }

    [Fact]
    public async Task Bulk_enabling_FULLGL_with_opening_balances_succeeds_and_turns_GL_VIEW_on()
    {
        await using var db = Install(openingJournal: true);

        var result = await Bulk(db).Handle(new BulkToggleCapabilitiesCommand(
            [new BulkToggleItem(FullGl, true, null)], null), default);

        IsOn(db, FullGl).Should().BeTrue();
        IsOn(db, GlView).Should().BeTrue();
        result.Should().Contain(r => r.Code == GlView && r.Enabled);
    }

    [Fact]
    public async Task Bulk_disabling_FULLGL_on_a_live_ledger_is_refused()
    {
        await using var db = LiveLedger();

        var act = () => Bulk(db).Handle(new BulkToggleCapabilitiesCommand(
            [new BulkToggleItem(Period, false, null), new BulkToggleItem(FullGl, false, null)], null), default);

        var ex = await act.Should().ThrowAsync<CapabilityMutationException>();
        ViolationCodes(ex.Which).Should().Contain("capability-gl-live-ledger");
        IsOn(db, FullGl).Should().BeTrue();
        IsOn(db, Period).Should().BeTrue();
    }

    [Fact]
    public async Task Validate_reports_the_live_ledger_violation()
    {
        await using var db = LiveLedger();

        var result = await new ValidateCapabilityChangesHandler(db, new DbSnapshot(db), Guard(db)).Handle(
            new ValidateCapabilityChangesCommand([new ValidateChangeItem(Period, false), new ValidateChangeItem(FullGl, false)]),
            default);

        result.Valid.Should().BeFalse();
        result.Violations.Should().Contain(v => v.Code == "capability-gl-live-ledger" && v.Capability == FullGl);
    }

    [Fact]
    public async Task Applying_Custom_on_a_live_ledger_keeps_the_ledger_and_its_dependents_on()
    {
        await using var db = LiveLedger();

        var result = await ApplyPreset(db).Handle(new ApplyPresetCommand("PRESET-CUSTOM", "re-apply"), default);

        result.Applied.Should().NotContain(d => d.Code == FullGl || d.Code == GlView || d.Code == Period || d.Code == Builtin);
        IsOn(db, FullGl).Should().BeTrue();
        IsOn(db, GlView).Should().BeTrue();
        IsOn(db, Period).Should().BeTrue();
        IsOn(db, Builtin).Should().BeTrue();
    }

    [Fact]
    public async Task Applying_an_external_accounting_preset_on_a_live_ledger_is_refused()
    {
        await using var db = LiveLedger();

        var act = () => ApplyPreset(db).Handle(new ApplyPresetCommand("PRESET-04", "switch"), default);

        var ex = await act.Should().ThrowAsync<CapabilityMutationException>();
        ViolationCodes(ex.Which).Should().Contain("capability-gl-live-ledger");
        IsOn(db, FullGl).Should().BeTrue();
        IsOn(db, Builtin).Should().BeTrue();
        IsOn(db, External).Should().BeFalse();
        IsOn(db, "CAP-QC-INSPECTION").Should().Be(CapabilityCatalog.All.Single(c => c.Code == "CAP-QC-INSPECTION").IsDefaultOn);
    }

    [Fact]
    public async Task Preset_preview_on_a_live_ledger_explains_the_refusal_and_keeps_the_ledger()
    {
        await using var db = LiveLedger();

        var preview = await new PreviewPresetApplyHandler(new DbSnapshot(db), Guard(db))
            .Handle(new PreviewPresetApplyQuery("PRESET-04"), default);

        preview!.Valid.Should().BeFalse();
        preview.Deltas.Should().NotContain(d => d.Code == FullGl || d.Code == Builtin || d.Code == GlView);
        var gl = preview.Violations.Single(v => v.Code == "capability-gl-live-ledger");
        gl.Capability.Should().Be(External);
        gl.Message.Should().NotContain("CAP-");
    }

    [Fact]
    public async Task Preset_detail_on_a_live_ledger_does_not_list_the_ledger_among_the_changes()
    {
        await using var db = LiveLedger();

        var detail = await new GetPresetDetailHandler(new DbSnapshot(db), Guard(db))
            .Handle(new GetPresetDetailQuery("PRESET-01"), default);

        detail!.DeltaVsCurrentInstall.Should().NotContain(d => d.Code == FullGl || d.Code == GlView || d.Code == Period);
    }

    [Fact]
    public async Task Custom_preview_lists_the_opening_balances_violation_for_a_FULLGL_override()
    {
        await using var db = Install(openingJournal: false);

        var preview = await new PreviewCustomPresetHandler(new DbSnapshot(db), Guard(db)).Handle(
            new PreviewCustomPresetCommand([new PresetCustomOverrideRequestItem(FullGl, true)]), default);

        preview.Valid.Should().BeFalse();
        preview.Violations.Should().Contain(v => v.Code == "capability-gl-opening-balances");
    }

    [Fact]
    public async Task Presets_keep_GL_VIEW_on_before_go_live_while_the_built_in_books_stay()
    {
        await using var db = Install(openingJournal: false, GlView);

        var preview = await new PreviewPresetApplyHandler(new DbSnapshot(db), Guard(db))
            .Handle(new PreviewPresetApplyQuery("PRESET-CUSTOM"), default);

        preview!.Deltas.Should().NotContain(d => d.Code == GlView);
    }

    [Fact]
    public void Holds_let_GL_VIEW_go_when_the_target_drops_the_built_in_books()
    {
        var current = new Dictionary<string, bool>(StringComparer.Ordinal) { [Builtin] = true, [GlView] = true };
        var target = new HashSet<string>(StringComparer.Ordinal) { External };

        PresetCapabilityHolds.Apply(target, current, ledgerLive: false);

        target.Should().NotContain(GlView);
    }

    [Fact]
    public void Live_ledger_holds_carry_FULLGL_dependents_and_their_dependencies()
    {
        var current = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [FullGl] = true,
            [Builtin] = true,
            ["CAP-IDEN-TENANT-CONFIG"] = true,
            ["CAP-ACCT-DEPRECIATION"] = true,
            ["CAP-MD-ASSETS"] = true,
            ["CAP-ACCT-FXREVAL"] = false,
        };

        var held = PresetCapabilityHolds.LiveLedgerHeld(current);

        held.Should().Contain([FullGl, GlView, Builtin, "CAP-IDEN-TENANT-CONFIG", "CAP-ACCT-DEPRECIATION", "CAP-MD-ASSETS"]);
        held.Should().NotContain("CAP-ACCT-FXREVAL");
    }

    [Fact]
    public async Task Discovery_apply_that_breaks_a_ledger_rule_writes_no_run()
    {
        await using var db = LiveLedger();
        var handler = new ApplyDiscoveryRecommendationHandler(
            db, new DbSnapshot(db), Mediator(db), Audit(), new FixedClock(), Guard(db));

        var act = () => handler.Handle(new ApplyDiscoveryRecommendationCommand(
            [new DiscoveryAnswerInput("Q-S1", "manufacturing")], "PRESET-04", false, null), default);

        await act.Should().ThrowAsync<CapabilityMutationException>();
        db.DiscoveryRuns.Count().Should().Be(0);
        IsOn(db, FullGl).Should().BeTrue();
    }

    [Fact]
    public void Discovery_deltas_on_a_live_ledger_leave_the_ledger_alone()
    {
        var current = CapabilityCatalog.All.ToDictionary(c => c.Code, c => c.IsDefaultOn, StringComparer.Ordinal);
        current[FullGl] = true;
        current[GlView] = true;

        var deltas = DiscoveryRecommendationEngine.ComputeDeltas("PRESET-CUSTOM", current, null, ledgerLive: true);

        deltas.Should().NotContain(d => d.Code == FullGl || d.Code == GlView);
    }
}
