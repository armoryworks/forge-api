using Forge.Core.Enums;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// Where a job disposed without a stock receipt sends its work in process at the job-cost close.
/// <see cref="JobDisposition.ShipToCustomer"/> closes it to cost of goods sold for <paramref name="ShippedQuantity"/>
/// of <paramref name="PartId"/>; <see cref="JobDisposition.Other"/> writes it off to
/// <paramref name="ExpenseGlAccountId"/>, or to production variance when none is chosen.
/// </summary>
public sealed record DisposedJobCostTarget(
    JobDisposition Disposition, int? PartId, decimal ShippedQuantity, int? ExpenseGlAccountId);
