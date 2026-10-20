using System.Text.Json;

namespace Forge.Core.Models;

public static class ApprovalActivationConditions
{
    public const string AmountGreaterThanKey = "amountGreaterThan";

    public static bool TryParse(string? json, out decimal? amountGreaterThan)
    {
        amountGreaterThan = null;
        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, AmountGreaterThanKey, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (property.Value.ValueKind == JsonValueKind.Null)
                    continue;

                if (property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetDecimal(out var threshold)
                    || threshold < 0)
                    return false;

                amountGreaterThan = threshold;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsValid(string? json) => TryParse(json, out _);
}
