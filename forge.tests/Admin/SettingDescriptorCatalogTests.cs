using FluentAssertions;

using Forge.Api.Features.CreditMemos;
using Forge.Api.Features.Invoices;
using Forge.Api.Services.Cpq;
using Forge.Core.Settings;

namespace Forge.Tests.Admin;

public class SettingDescriptorCatalogTests
{
    [Theory]
    [InlineData(CreateInvoiceHandler.FreightItemSettingKey, "QuickBooks items")]
    [InlineData(CreditMemoSync.BadDebtItemSettingKey, "QuickBooks items")]
    [InlineData(CreditMemoSync.RefundItemSettingKey, "QuickBooks items")]
    [InlineData(CreditMemoSync.RefundAccountSettingKey, "QuickBooks items")]
    [InlineData(BuiltInCpqService.QuoteValidDaysKey, "Sales")]
    public void Settings_read_by_the_api_are_in_the_catalog_so_admin_settings_can_set_them(string key, string group)
    {
        var descriptor = SettingDescriptorCatalog.FindByKey(key);

        descriptor.Should().NotBeNull();
        descriptor!.Group.Should().Be(group);
    }

    [Fact]
    public void Setting_keys_are_unique()
    {
        SettingDescriptorCatalog.All.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key)
            .Should().BeEmpty();
    }

    [Fact]
    public void Quote_validity_defaults_to_the_cpq_default()
        => SettingDescriptorCatalog.FindByKey(QuoteSettings.DefaultValidDaysKey)!.DefaultValue
            .Should().Be(BuiltInCpqService.DefaultQuoteValidDays.ToString());
}
