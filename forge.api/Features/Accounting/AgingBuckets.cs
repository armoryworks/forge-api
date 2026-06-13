namespace Forge.Api.Features.Accounting;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — the aging-bucket ladder for AR aging, AP aging, and GRNI aging,
/// derived from the <c>accounting.aging.bucket-days</c> setting (default "30,60,90" → the
/// industry-standard 0-30 / 31-60 / 61-90 / 91+). The setting is a comma-separated ascending
/// list of inclusive upper bounds; the final open-ended bucket is added automatically. A
/// malformed value falls back to the standard ladder rather than breaking the reports.
/// </summary>
public static class AgingBuckets
{
    public const string DefaultBucketDays = "30,60,90";

    public static readonly IReadOnlyList<(int From, int? To, string Label)> Standard = Parse(DefaultBucketDays);

    public static IReadOnlyList<(int From, int? To, string Label)> Parse(string? bucketDays)
    {
        var bounds = (bucketDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : -1)
            .ToList();

        // Validate: at least one bound, all positive, strictly ascending — else the standard ladder.
        if (bounds.Count == 0 || bounds.Any(b => b <= 0) || bounds.Zip(bounds.Skip(1)).Any(p => p.Second <= p.First))
            return bucketDays == DefaultBucketDays
                ? BuildLadder([30, 60, 90])
                : Parse(DefaultBucketDays);

        return BuildLadder(bounds);
    }

    private static List<(int From, int? To, string Label)> BuildLadder(IReadOnlyList<int> bounds)
    {
        var buckets = new List<(int From, int? To, string Label)>(bounds.Count + 1);
        var from = 0;
        foreach (var to in bounds)
        {
            buckets.Add((from, to, $"{from}-{to}"));
            from = to + 1;
        }
        buckets.Add((from, null, $"{from}+"));
        return buckets;
    }
}
