namespace Forge.Core.Settings;

/// <summary>
/// Early-payment discount settings. <c>terms.discount_base</c> picks the amount the discount percent applies to
/// (the whole document total, the QuickBooks behaviour, or the subtotal before tax); <c>terms.discount_grace_days</c>
/// keeps the discount open for that many days after the discount date.
/// </summary>
public static class PaymentTermsSettings
{
    public const string DiscountBaseKey = "terms.discount_base";
    public const string DiscountGraceDaysKey = "terms.discount_grace_days";

    public const string BaseTotal = "Total";
    public const string BaseSubtotal = "Subtotal";

    public static IReadOnlyList<SettingDescriptor> Descriptors { get; } = new[]
    {
        new SettingDescriptor(
            Key: DiscountBaseKey,
            Group: "Payments",
            DisplayName: "Early-payment discount base",
            DataType: SettingDataType.Enum,
            Description: "The amount an early-payment discount percent applies to.",
            DefaultValue: BaseTotal,
            SortOrder: 20,
            Choices: new[]
            {
                new EnumChoice(BaseTotal, "Document total, including tax and freight"),
                new EnumChoice(BaseSubtotal, "Subtotal, before tax"),
            }),
        new SettingDescriptor(
            Key: DiscountGraceDaysKey,
            Group: "Payments",
            DisplayName: "Early-payment discount grace days",
            DataType: SettingDataType.Integer,
            Description: "Days after the discount date that a payment still takes the discount.",
            DefaultValue: "0",
            ValidationPattern: @"^\d{1,3}$",
            SortOrder: 30),
    };
}
