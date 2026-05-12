namespace Forge.Core.Models;

public class SsoOptions
{
    public SsoProviderOptions Google { get; set; } = new();
    public SsoProviderOptions Microsoft { get; set; } = new();
    public SsoProviderOptions Oidc { get; set; } = new();
}
