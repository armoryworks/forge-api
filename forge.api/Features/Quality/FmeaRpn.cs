using Forge.Core.Entities;

namespace Forge.Api.Features.Quality;

/// <summary>
/// Risk priority numbers for an FMEA item. A recorded action may revise any of severity, occurrence and
/// detection; a rating it leaves unrevised carries over from the original, so the revised RPN exists as soon as
/// one rating is revised. The current RPN is the revised one when there is a revision and the original otherwise,
/// and it is what the FMEA's high-RPN counts, maximum and risk summary measure.
/// </summary>
public static class FmeaRpn
{
    public static int Original(FmeaItem item) => item.Severity * item.Occurrence * item.Detection;

    public static bool IsRevised(FmeaItem item)
        => item.RevisedSeverity.HasValue || item.RevisedOccurrence.HasValue || item.RevisedDetection.HasValue;

    public static (int Severity, int Occurrence, int Detection) CurrentRatings(FmeaItem item)
        => (item.RevisedSeverity ?? item.Severity,
            item.RevisedOccurrence ?? item.Occurrence,
            item.RevisedDetection ?? item.Detection);

    public static int? Revised(FmeaItem item)
    {
        if (!IsRevised(item))
            return null;
        var (s, o, d) = CurrentRatings(item);
        return s * o * d;
    }

    public static int Current(FmeaItem item) => Revised(item) ?? Original(item);
}
