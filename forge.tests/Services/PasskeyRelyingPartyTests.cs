using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using Forge.Api.Services;

namespace Forge.Tests.Services;

public class PasskeyRelyingPartyTests
{
    private static HttpRequest Request(string host, string? origin = null, string scheme = "http")
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        if (origin is not null) context.Request.Headers.Origin = origin;
        return context.Request;
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Rp_id_is_the_host_without_port_and_the_browser_origin_on_that_host_is_allowed()
    {
        var rp = PasskeyRelyingParty.Resolve(Config(), Request("localhost", "http://localhost:4510"));

        rp.RpId.Should().Be("localhost");
        rp.Origins.Should().BeEquivalentTo("http://localhost", "http://localhost:4510");
    }

    [Fact]
    public void A_browser_origin_on_another_host_is_not_allowed()
    {
        var rp = PasskeyRelyingParty.Resolve(Config(), Request("shop.example.com", "https://evil.example.com"));

        rp.Origins.Should().BeEquivalentTo("http://shop.example.com");
    }

    [Fact]
    public void Configured_public_origins_on_the_rp_host_are_allowed()
    {
        var rp = PasskeyRelyingParty.Resolve(
            Config(
                ("FrontendBaseUrl", "https://shop.example.com:8443/"),
                ("Passkeys:Origins", "https://shop.example.com:9443, https://other.example.com")),
            Request("shop.example.com", scheme: "https"));

        rp.Origins.Should().BeEquivalentTo(
            "https://shop.example.com",
            "https://shop.example.com:8443",
            "https://shop.example.com:9443");
    }

    [Fact]
    public void Configured_origin_arrays_are_read()
    {
        var rp = PasskeyRelyingParty.Resolve(
            Config(("Passkeys:Origins:0", "https://shop.example.com:9443")),
            Request("shop.example.com", scheme: "https"));

        rp.Origins.Should().Contain("https://shop.example.com:9443");
    }

    [Fact]
    public void A_non_web_origin_is_ignored()
    {
        var rp = PasskeyRelyingParty.Resolve(Config(), Request("localhost", "file://localhost"));

        rp.Origins.Should().BeEquivalentTo("http://localhost");
    }
}
