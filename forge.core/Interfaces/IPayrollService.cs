using Forge.Core.Models.Accounting;

namespace Forge.Core.Interfaces;

/// <summary>
/// ⚡ Phase-5 — payroll GL foundation. Create pay runs (amounts provided) and post the payroll journal on
/// approval (Dr wage/employer-tax expense; Cr employee-tax-payable, employer-tax-payable, net-pay-payable).
/// Tax calculation is the §8.3 build-vs-integrate spike, out of this foundation.
/// </summary>
public interface IPayrollService
{
    Task<PayRunModel> CreatePayRunAsync(CreatePayRunModel model, CancellationToken ct = default);
    Task<IReadOnlyList<PayRunModel>> ListAsync(int bookId, CancellationToken ct = default);

    /// <summary>Posts the payroll journal for a Draft pay run and marks it Posted. Idempotent.</summary>
    Task<PayRunModel> PostPayRunAsync(int payRunId, int postedByUserId, CancellationToken ct = default);
}
