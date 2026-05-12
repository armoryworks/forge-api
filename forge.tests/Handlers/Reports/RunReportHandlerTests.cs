using FluentAssertions;

using Forge.Api.Features.Reports;
using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Reports;

public class RunReportHandlerTests
{
    [Fact]
    public async Task Handle_CustomersSource_ReturnsProjectedRows()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        db.Customers.AddRange(
            new Customer { Name = "Acme Corp", IsActive = true },
            new Customer { Name = "Globex Inc", IsActive = true },
            new Customer { Name = "Initech", IsActive = false });
        await db.SaveChangesAsync();

        var handler = new RunReportHandler(db);
        var command = new RunReportCommand(
            "Customers", ["Name", "IsActive"], null, null, "Name", "asc", 1, 100);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(3);
        result.Rows.Should().HaveCount(3);
        result.Columns.Should().Contain("Name");
        result.Rows.First()["Name"].Should().Be("Acme Corp");
    }

    [Fact]
    public async Task Handle_InvalidEntitySource_ThrowsArgumentException()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        var handler = new RunReportHandler(db);
        var command = new RunReportCommand(
            "InvalidSource", ["Id"], null, null, null, null, null, null);

        // Act
        var act = () => handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Unknown entity source*");
    }

    [Fact]
    public async Task Handle_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        using var db = TestDbContextFactory.Create();
        for (var i = 1; i <= 10; i++)
            db.Customers.Add(new Customer { Name = $"Customer {i:D2}", IsActive = true });
        await db.SaveChangesAsync();

        var handler = new RunReportHandler(db);
        var command = new RunReportCommand(
            "Customers", ["Name"], null, null, "Name", "asc", 2, 3);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(10);
        result.Rows.Should().HaveCount(3);
        result.Rows.First()["Name"].Should().Be("Customer 04");
    }
}
