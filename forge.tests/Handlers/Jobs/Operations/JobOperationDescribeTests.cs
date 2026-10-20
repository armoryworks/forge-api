using FluentAssertions;

using Forge.Api.Extensions;
using Forge.Api.Features.Jobs.Operations;
using Forge.Core.Entities;
using Forge.Core.Enums;

namespace Forge.Tests.Handlers.Jobs.Operations;

public class JobOperationDescribeTests
{
    [Fact]
    public void Describe_HumanizesTheStatus()
    {
        var row = new JobOperation { Status = JobOperationStatus.InProgress, CompletedQuantity = 20m };

        JobOperationRules.Describe(row, 40m).Should().Be("In progress 20/40");
    }

    [Theory]
    [InlineData(SalesOrderStatus.PartiallyShipped, "Partially shipped")]
    [InlineData(JobOperationStatus.InProgress, "In progress")]
    [InlineData(JobOperationStatus.Complete, "Complete")]
    public void ToSentence_SplitsPascalCase(Enum value, string expected) =>
        value.ToSentence().Should().Be(expected);
}
