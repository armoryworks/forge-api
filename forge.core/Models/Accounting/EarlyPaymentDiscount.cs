namespace Forge.Core.Models.Accounting;

/// <summary>
/// Date and amount rules for payment terms with an early-payment discount. Dates count calendar days in the
/// document date's own offset; amounts round like every other amount of money (<see cref="Money.Round"/>).
/// </summary>
public static class EarlyPaymentDiscount
{
    /// <summary>The due date: the document date plus the terms' net days.</summary>
    public static DateTimeOffset DueDate(DateTimeOffset documentDate, int netDays)
        => documentDate.AddDays(netDays);

    /// <summary>The last day the discount may be taken, or null when the terms carry no discount.</summary>
    public static DateTimeOffset? DiscountDueDate(DateTimeOffset documentDate, decimal discountPercent, int discountDays)
        => discountPercent > 0m && discountDays > 0 ? documentDate.AddDays(discountDays) : null;

    /// <summary>True when <paramref name="dueDate"/> falls exactly <paramref name="netDays"/> calendar days after <paramref name="documentDate"/>.</summary>
    public static bool MatchesDueDate(DateTimeOffset documentDate, DateTimeOffset dueDate, int netDays)
        => (dueDate.ToOffset(documentDate.Offset).Date - documentDate.Date).Days == netDays;

    /// <summary>The last day a payment still earns the discount: the discount date plus the grace days.</summary>
    public static DateTimeOffset? Deadline(DateTimeOffset? discountDueDate, int graceDays)
        => discountDueDate?.AddDays(Math.Max(0, graceDays));

    /// <summary>True when a payment dated <paramref name="paymentDate"/> is on or before the deadline (by calendar day).</summary>
    public static bool InWindow(DateTimeOffset? discountDueDate, DateTimeOffset paymentDate, int graceDays)
        => Deadline(discountDueDate, graceDays) is { } deadline
           && paymentDate.ToOffset(deadline.Offset).Date <= deadline.Date;

    /// <summary>The whole discount the terms allow on <paramref name="baseAmount"/>.</summary>
    public static decimal Maximum(decimal baseAmount, decimal discountPercent)
        => discountPercent <= 0m || baseAmount <= 0m ? 0m : Money.Round(baseAmount * discountPercent / 100m);

    /// <summary>
    /// The discount a cash application earns automatically. A payment that settles the balance once the rest of
    /// the allowed discount is taken gets that rest (never more than the balance left after the cash); a smaller
    /// payment earns <c>cash × pct / (1 − pct)</c>, capped at the remaining maximum.
    /// </summary>
    public static decimal Automatic(decimal cash, decimal balanceDue, decimal remainingMaximum, decimal discountPercent)
    {
        if (cash <= 0m || remainingMaximum <= 0m || discountPercent <= 0m || discountPercent >= 100m || balanceDue <= 0m)
            return 0m;

        if (cash + remainingMaximum >= balanceDue)
            return Math.Max(0m, Math.Min(remainingMaximum, balanceDue - cash));

        var rate = discountPercent / 100m;
        return Math.Min(remainingMaximum, Money.Round(cash * rate / (1m - rate)));
    }
}
