namespace Forge.Core.Models;

public class WaveOptions
{
    public const string SectionName = "Wave";

    /// <summary>Wave personal access token (or OAuth2 access token).</summary>
    public string AccessToken { get; set; } = string.Empty;
    public string BusinessId { get; set; } = string.Empty;
    public string GraphQlUrl { get; } = "https://gql.waveapps.com/graphql/public";
}
