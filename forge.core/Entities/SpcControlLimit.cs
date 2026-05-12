namespace Forge.Core.Entities;

public class SpcControlLimit : BaseEntity
{
    public int CharacteristicId { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public int SampleCount { get; set; }
    public int FromSubgroup { get; set; }
    public int ToSubgroup { get; set; }
    public decimal XBarUcl { get; set; }
    public decimal XBarLcl { get; set; }
    public decimal XBarCenterLine { get; set; }
    public decimal RangeUcl { get; set; }
    public decimal RangeLcl { get; set; }
    public decimal RangeCenterLine { get; set; }
    public decimal? SUcl { get; set; }
    public decimal? SLcl { get; set; }
    public decimal? SCenterLine { get; set; }
    public decimal Cp { get; set; }
    public decimal Cpk { get; set; }
    public decimal Pp { get; set; }
    public decimal Ppk { get; set; }
    public decimal ProcessSigma { get; set; }
    public bool IsActive { get; set; } = true;

    public SpcCharacteristic Characteristic { get; set; } = null!;
}
