namespace Forge.Core.Models;

public class NetSuiteOptions
{
    public const string SectionName = "NetSuite";

    /// <summary>NetSuite account ID (e.g., "1234567" or "1234567_SB1" for sandbox).</summary>
    public string AccountId { get; set; } = string.Empty;
    public string ConsumerKey { get; set; } = string.Empty;
    public string ConsumerSecret { get; set; } = string.Empty;
    public string TokenId { get; set; } = string.Empty;
    public string TokenSecret { get; set; } = string.Empty;

    public string BaseUrl => string.IsNullOrEmpty(AccountId)
        ? string.Empty
        : $"https://{AccountId.Replace("_", "-").ToLowerInvariant()}.suitetalk.api.netsuite.com/services/rest/record/v1";
}
