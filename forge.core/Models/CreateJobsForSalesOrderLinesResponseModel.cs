namespace Forge.Core.Models;

public record CreateJobsForSalesOrderLinesResponseModel(
    int Created,
    List<SkippedSalesOrderLineResponseModel> Skipped);
