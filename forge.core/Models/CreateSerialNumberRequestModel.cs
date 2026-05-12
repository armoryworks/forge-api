namespace Forge.Core.Models;

public class CreateSerialNumberRequestModel
{
    public string SerialValue { get; set; } = string.Empty;
    public int? JobId { get; set; }
    public int? LotRecordId { get; set; }
    public int? CurrentLocationId { get; set; }
    public int? ParentSerialId { get; set; }
    public string? Notes { get; set; }
}
