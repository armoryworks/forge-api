
namespace Forge.Core.Entities;

public class PayStubDeduction : BaseEntity
{
    public int PayStubId { get; set; }
    public PayStubDeductionCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public PayStub PayStub { get; set; } = null!;
}
