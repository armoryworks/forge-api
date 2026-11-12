using AwesomeAssertions;
using FluentValidation;
using MediatR;
using Moq;

using Forge.Api.Features.Reports;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Reports;

public class ExportReportHandlerTests
{
    private const int OwnerId = 7;
    private const int OtherUserId = 8;

    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly List<RunReportCommand> _pages = [];
    private readonly FakeHtmlPdfRenderer _renderer = new();

    private ExportReportHandler CreateHandler()
    {
        var runHandler = new RunReportHandler(_db);
        var validator = new RunReportCommandValidator();
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(m => m.Send(It.IsAny<RunReportCommand>(), It.IsAny<CancellationToken>()))
            .Returns(async (RunReportCommand command, CancellationToken ct) =>
            {
                _pages.Add(command);
                await validator.ValidateAndThrowAsync(command, ct);
                return await runHandler.Handle(command, ct);
            });

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        return new ExportReportHandler(_db, mediator.Object, clock.Object, _renderer);
    }

    private async Task<SavedReport> SeedReportAsync(bool isShared = false, string? filtersJson = null, string name = "Customers")
    {
        var report = new SavedReport
        {
            Name = name,
            EntitySource = "Customers",
            ColumnsJson = """["Name","IsActive"]""",
            FiltersJson = filtersJson,
            UserId = OwnerId,
            IsShared = isShared,
        };
        _db.SavedReports.Add(report);
        await _db.SaveChangesAsync();
        return report;
    }

    private static int CsvDataLines(ExportReportResult result) =>
        System.Text.Encoding.UTF8.GetString(result.Content)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;

    [Fact]
    public async Task Export_pages_through_every_row_without_exceeding_the_run_page_limit()
    {
        for (var i = 1; i <= 1500; i++)
            _db.Customers.Add(new Customer { Name = $"Customer {i:D4}", IsActive = true });
        var report = await SeedReportAsync();

        var result = await CreateHandler().Handle(
            new ExportReportQuery(report.Id, ReportExportFormat.Csv, OwnerId), CancellationToken.None);

        CsvDataLines(result).Should().Be(1500);
        _pages.Should().HaveCount(2);
        _pages.Should().OnlyContain(p => p.PageSize <= 1000);
    }

    [Fact]
    public async Task Export_applies_the_saved_filters()
    {
        _db.Customers.AddRange(
            new Customer { Name = "Active A", IsActive = true },
            new Customer { Name = "Active B", IsActive = true },
            new Customer { Name = "Inactive", IsActive = false });
        var report = await SeedReportAsync(filtersJson: """[{"Field":"IsActive","Operator":"Equals","Value":"true"}]""");

        var result = await CreateHandler().Handle(
            new ExportReportQuery(report.Id, ReportExportFormat.Csv, OwnerId), CancellationToken.None);

        CsvDataLines(result).Should().Be(2);
        System.Text.Encoding.UTF8.GetString(result.Content).Should().NotContain("Inactive");
    }

    [Fact]
    public async Task Export_of_another_users_private_report_is_not_found()
    {
        var report = await SeedReportAsync();

        var act = () => CreateHandler().Handle(
            new ExportReportQuery(report.Id, ReportExportFormat.Csv, OtherUserId), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        _pages.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_of_another_users_shared_report_succeeds()
    {
        _db.Customers.Add(new Customer { Name = "Shared Co", IsActive = true });
        var report = await SeedReportAsync(isShared: true);

        var result = await CreateHandler().Handle(
            new ExportReportQuery(report.Id, ReportExportFormat.Csv, OtherUserId), CancellationToken.None);

        CsvDataLines(result).Should().Be(1);
    }

    [Fact]
    public async Task System_export_without_a_requesting_user_skips_the_owner_check()
    {
        _db.Customers.Add(new Customer { Name = "Scheduled Co", IsActive = true });
        var report = await SeedReportAsync();

        var result = await CreateHandler().Handle(
            new ExportReportQuery(report.Id, ReportExportFormat.Csv), CancellationToken.None);

        CsvDataLines(result).Should().Be(1);
    }

    [Fact]
    public async Task Pdf_export_prints_the_report_table_landscape_through_the_renderer()
    {
        _db.Customers.AddRange(
            new Customer { Name = "Zulu Works", IsActive = true },
            new Customer { Name = "Alpha Works", IsActive = false });
        var report = await SeedReportAsync();

        var result = await CreateHandler().Handle(
            new ExportReportQuery(report.Id, ReportExportFormat.Pdf, OwnerId), CancellationToken.None);

        result.ContentType.Should().Be("application/pdf");
        result.FileName.Should().Be("Customers.pdf");
        result.Content.Should().StartWith("%PDF-"u8.ToArray());
        var html = _renderer.Documents.Should().ContainSingle().Subject.Html;
        html.Should().Contain("<body class=\"landscape\">");
        html.Should().Contain("<th>Name</th>").And.Contain("<th>IsActive</th>");
        html.Should().Contain("Zulu Works").And.Contain("Alpha Works").And.Contain(">Yes<").And.Contain(">No<");
        html.Should().Contain("Generated: Oct 19, 2026 12:00 UTC").And.Contain("2 records");
    }

    [Fact]
    public async Task Pdf_export_of_an_empty_report_says_there_is_no_data()
    {
        var report = await SeedReportAsync();

        await CreateHandler().Handle(new ExportReportQuery(report.Id, ReportExportFormat.Pdf, OwnerId), CancellationToken.None);

        var html = _renderer.Documents.Should().ContainSingle().Subject.Html;
        html.Should().Contain("No data to display.").And.NotContain("<table");
    }

    [Fact]
    public async Task Pdf_export_encodes_every_value_a_user_typed()
    {
        _db.Customers.Add(new Customer { Name = "<script>alert(1)</script> <b>bold</b>", IsActive = true });
        var report = await SeedReportAsync(name: "Customers <b>& co</b>");

        await CreateHandler().Handle(new ExportReportQuery(report.Id, ReportExportFormat.Pdf, OwnerId), CancellationToken.None);

        var html = _renderer.Documents.Should().ContainSingle().Subject.Html;
        html.Should().NotContain("<script").And.NotContain("<b>");
        html.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt; &lt;b&gt;bold&lt;/b&gt;");
        html.Should().Contain("<div class=\"page-footer\">Customers &lt;b&gt;&amp; co&lt;/b&gt; &middot; Page ");
    }

    [Fact]
    public void The_footer_carries_the_report_name()
    {
        var html = ReportPdfDocument.Build(
            new RunReportResponseModel(["Name"], [], 0, null), "Open orders", DateTimeOffset.UnixEpoch).Html;

        html.Should().Contain("<div class=\"page-footer\">Open orders &middot; Page ");
    }

    [Fact]
    public void Prints_at_most_fifteen_columns_in_their_saved_order()
    {
        var columns = Enumerable.Range(1, 17).Select(i => $"C{i}").ToArray();
        var row = columns.ToDictionary(c => c, c => (object?)c);
        var html = ReportPdfDocument.Build(
            new RunReportResponseModel(columns, [row], 1, null), "Wide", DateTimeOffset.UnixEpoch).Html;

        html.Should().Contain("<th>C15</th>").And.NotContain("<th>C16</th>");
        html.IndexOf("<th>C2</th>", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("<th>C10</th>", StringComparison.Ordinal));
    }
}
