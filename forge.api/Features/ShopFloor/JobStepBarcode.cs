using System.Globalization;

namespace Forge.Api.Features.ShopFloor;

/// <summary>
/// The barcode printed for one routing step of a job: <c>FJO:{jobId}.{operationId}</c>. Scanning it opens the
/// job's step view at that step on the kiosk.
/// </summary>
public static class JobStepBarcode
{
    public const string Prefix = "FJO:";

    public static string Format(int jobId, int operationId)
        => string.Create(CultureInfo.InvariantCulture, $"{Prefix}{jobId}.{operationId}");

    public static bool TryParse(string value, out int jobId, out int operationId)
    {
        jobId = 0;
        operationId = 0;
        if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = value[Prefix.Length..].Split('.');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out jobId)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out operationId)
            && jobId > 0
            && operationId > 0;
    }
}
