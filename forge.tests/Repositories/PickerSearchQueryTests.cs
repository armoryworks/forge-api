using FluentAssertions;
using Moq;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Repositories;

[Collection(PostgresCollection.Name)]
public sealed class PickerSearchQueryTests(PostgresFixture fixture)
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static string Token() => $"z{Guid.NewGuid():N}"[..10];

    private static PartRepository PartRepo(AppDbContext db)
    {
        var pricing = new Mock<IPartPricingResolver>();
        pricing.Setup(p => p.ResolveManyAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new ResolvedPartPrice(id, 0m, "USD", PartPriceSource.Default, null, null)));
        return new PartRepository(db, pricing.Object);
    }

    private static async Task<int> SeedCurrencyAsync(AppDbContext db)
    {
        var currency = new Currency { Code = $"P{Guid.NewGuid():N}"[..8], Name = "Picker Dollar", Symbol = "$" };
        db.Set<Currency>().Add(currency);
        await db.SaveChangesAsync();
        return currency.Id;
    }

    [Fact]
    public async Task Part_search_ranks_exact_then_number_prefix_then_name_prefix_ahead_of_newer_parts()
    {
        await using var db = fixture.CreateContext();
        var t = Token();
        var now = DateTimeOffset.UtcNow;
        db.Parts.AddRange(
            new Part { PartNumber = $"X-{t}", Name = $"contains {t}", CreatedAt = now.AddMinutes(5) },
            new Part { PartNumber = $"N-{t}", Name = $"{t} widget", CreatedAt = now.AddMinutes(4) },
            new Part { PartNumber = $"{t}-02", Name = "Prefix", CreatedAt = now.AddMinutes(3) },
            new Part { PartNumber = t.ToUpper(), Name = "Exact", CreatedAt = now });
        await db.SaveChangesAsync();

        var page = await PartRepo(db).GetPagedAsync(new PartListQuery { Q = t, PageSize = 10 }, CancellationToken.None);

        page.Items.Select(p => p.Name).Should().Equal("Exact", "Prefix", $"{t} widget", $"contains {t}");
    }

    [Fact]
    public async Task Part_search_keeps_an_explicit_sort()
    {
        await using var db = fixture.CreateContext();
        var t = Token();
        db.Parts.AddRange(
            new Part { PartNumber = $"B-{t}", Name = "b" },
            new Part { PartNumber = t, Name = "exact" },
            new Part { PartNumber = $"A-{t}", Name = "a" });
        await db.SaveChangesAsync();

        var page = await PartRepo(db).GetPagedAsync(
            new PartListQuery { Q = t, Sort = "partNumber", Order = "asc" }, CancellationToken.None);

        page.Items.Select(p => p.PartNumber).Should().Equal($"A-{t}", $"B-{t}", t);
    }

    [Fact]
    public async Task Customer_search_ranks_exact_and_prefix_names_first()
    {
        await using var db = fixture.CreateContext();
        var t = Token();
        var now = DateTimeOffset.UtcNow;
        db.Customers.AddRange(
            new Customer { Name = $"Acme {t}", CreatedAt = now.AddMinutes(3) },
            new Customer { Name = $"{t} Holdings", CreatedAt = now.AddMinutes(2) },
            new Customer { Name = t, CreatedAt = now });
        await db.SaveChangesAsync();

        var page = await new CustomerRepository(db).GetPagedAsync(
            new CustomerListQuery { Q = t }, CancellationToken.None);

        page.Items.Select(c => c.Name).Should().Equal(t, $"{t} Holdings", $"Acme {t}");
    }

    [Fact]
    public async Task Job_search_puts_the_exact_job_number_inside_a_small_page()
    {
        await using var db = fixture.CreateContext();
        var t = Token();
        var track = new TrackType { Name = "Picker", Code = Token(), IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "S", Code = "s", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 12; i++)
            db.Jobs.Add(new Job { JobNumber = $"{t}-{i:D2}", Title = "newer", TrackTypeId = track.Id, CurrentStageId = stage.Id, CreatedAt = now.AddMinutes(i + 1) });
        db.Jobs.Add(new Job { JobNumber = t, Title = "exact", TrackTypeId = track.Id, CurrentStageId = stage.Id, CreatedAt = now });
        await db.SaveChangesAsync();

        var page = await new JobRepository(db, new FixedClock(now)).GetPagedJobsAsync(
            new JobListQuery { Q = t, PageSize = 10 }, false, CancellationToken.None);

        page.TotalCount.Should().Be(13);
        page.Items.First().Title.Should().Be("exact");
    }

    [Fact]
    public async Task Include_archived_returns_archived_and_live_jobs()
    {
        await using var db = fixture.CreateContext();
        var track = new TrackType { Name = "Archive", Code = Token(), IsActive = true };
        db.TrackTypes.Add(track);
        await db.SaveChangesAsync();
        var stage = new JobStage { TrackTypeId = track.Id, Name = "S", Code = "s", SortOrder = 1, IsActive = true };
        db.JobStages.Add(stage);
        await db.SaveChangesAsync();
        db.Jobs.AddRange(
            new Job { JobNumber = Token(), Title = "live", TrackTypeId = track.Id, CurrentStageId = stage.Id },
            new Job { JobNumber = Token(), Title = "archived", TrackTypeId = track.Id, CurrentStageId = stage.Id, IsArchived = true });
        await db.SaveChangesAsync();
        var repo = new JobRepository(db, new FixedClock(DateTimeOffset.UtcNow));

        var standard = await repo.GetPagedJobsAsync(new JobListQuery { TrackTypeId = track.Id }, false, CancellationToken.None);
        var all = await repo.GetPagedJobsAsync(new JobListQuery { TrackTypeId = track.Id, IncludeArchived = true }, false, CancellationToken.None);

        standard.Items.Select(j => j.Title).Should().Equal("live");
        all.Items.Select(j => j.Title).Should().BeEquivalentTo("live", "archived");
    }

    [Fact]
    public async Task Open_only_reaches_open_invoices_past_the_200_row_page_cap()
    {
        await using var db = fixture.CreateContext();
        var currencyId = await SeedCurrencyAsync(db);
        var customer = new Customer { Name = $"Many invoices {Token()}" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var oldest = DateTimeOffset.UtcNow.AddDays(-400);
        Invoice NewInvoice(string number, InvoiceStatus status, decimal amount, int ageDays) => new()
        {
            InvoiceNumber = number,
            CustomerId = customer.Id,
            CurrencyId = currencyId,
            Status = status,
            InvoiceDate = oldest.AddDays(ageDays),
            DueDate = oldest.AddDays(ageDays + 30),
            CreatedAt = oldest.AddDays(ageDays),
            Lines = amount == 0 ? [] : [new InvoiceLine { Description = "Line", Quantity = 1, UnitPrice = amount, LineNumber = 1 }],
        };

        var prefix = Token();
        db.Invoices.Add(NewInvoice($"{prefix}-OLD-OPEN", InvoiceStatus.Sent, 100m, 0));
        db.Invoices.Add(NewInvoice($"{prefix}-DRAFT", InvoiceStatus.Draft, 100m, 1));
        db.Invoices.Add(NewInvoice($"{prefix}-VOID", InvoiceStatus.Voided, 100m, 2));
        db.Invoices.Add(NewInvoice($"{prefix}-ZERO", InvoiceStatus.Sent, 0m, 3));
        var paid = NewInvoice($"{prefix}-PAID", InvoiceStatus.Paid, 50m, 4);
        db.Invoices.Add(paid);
        for (var i = 0; i < 205; i++)
            db.Invoices.Add(NewInvoice($"{prefix}-NEW-{i:D3}", InvoiceStatus.Draft, 10m, 10 + i));
        await db.SaveChangesAsync();

        var payment = new Payment { PaymentNumber = Token(), CustomerId = customer.Id, Amount = 50m, PaymentDate = DateTimeOffset.UtcNow };
        payment.Applications.Add(new PaymentApplication { InvoiceId = paid.Id, Amount = 50m });
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var repo = new InvoiceRepository(db);
        var capped = await repo.GetPagedAsync(new InvoiceListQuery { CustomerId = customer.Id, PageSize = 500 }, CancellationToken.None);
        var open = await repo.GetPagedAsync(new InvoiceListQuery { CustomerId = customer.Id, OpenOnly = true, PageSize = 500 }, CancellationToken.None);

        capped.TotalCount.Should().Be(210);
        capped.Items.Should().HaveCount(200);
        capped.Items.Select(i => i.InvoiceNumber).Should().NotContain($"{prefix}-OLD-OPEN");
        open.TotalCount.Should().Be(1);
        open.Items.Select(i => i.InvoiceNumber).Should().Equal($"{prefix}-OLD-OPEN");
        open.Items.Single().BalanceDue.Should().Be(100m);
    }
}
