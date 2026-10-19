using System.Security.Cryptography;
using System.Text;

using FluentAssertions;

using Forge.Api.Features.ComplianceForms;

namespace Forge.Tests.Handlers.ComplianceForms;

public class DocuSealWebhookSignatureTests
{
    private const string Secret = "whsec_test_secret";
    private const string Body = """{"event_type":"form.completed","data":{"id":9001}}""";
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static string Hex(string secret, string content)
        => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(content)));

    private static string DocuSealHeader(long timestamp, string secret = Secret, string body = Body)
        => $"{timestamp}.{Hex(secret, $"{timestamp}.{body}").ToLowerInvariant()}";

    [Fact]
    public void DocuSealFormat_LowerHex_IsValid()
        => DocuSealWebhookSignature.IsValid(Secret, Body, DocuSealHeader(Now.ToUnixTimeSeconds()), Now).Should().BeTrue();

    [Fact]
    public void DocuSealFormat_UpperHex_IsValid()
    {
        var ts = Now.ToUnixTimeSeconds();
        var header = $"{ts}.{Hex(Secret, $"{ts}.{Body}")}";

        DocuSealWebhookSignature.IsValid(Secret, Body, header, Now).Should().BeTrue();
    }

    [Fact]
    public void DocuSealFormat_WithinTolerance_IsValid()
        => DocuSealWebhookSignature.IsValid(Secret, Body, DocuSealHeader(Now.ToUnixTimeSeconds() - 299), Now).Should().BeTrue();

    [Fact]
    public void DocuSealFormat_StaleTimestamp_IsRejected()
        => DocuSealWebhookSignature.IsValid(Secret, Body, DocuSealHeader(Now.ToUnixTimeSeconds() - 301), Now).Should().BeFalse();

    [Fact]
    public void DocuSealFormat_FutureTimestamp_IsRejected()
        => DocuSealWebhookSignature.IsValid(Secret, Body, DocuSealHeader(Now.ToUnixTimeSeconds() + 301), Now).Should().BeFalse();

    [Fact]
    public void DocuSealFormat_WrongSecret_IsRejected()
        => DocuSealWebhookSignature.IsValid(Secret, Body, DocuSealHeader(Now.ToUnixTimeSeconds(), "whsec_other"), Now).Should().BeFalse();

    [Fact]
    public void DocuSealFormat_TamperedBody_IsRejected()
        => DocuSealWebhookSignature.IsValid(Secret, Body + " ", DocuSealHeader(Now.ToUnixTimeSeconds()), Now).Should().BeFalse();

    [Fact]
    public void DocuSealFormat_TimestampSwappedAfterSigning_IsRejected()
    {
        var ts = Now.ToUnixTimeSeconds();
        var signature = Hex(Secret, $"{ts - 1000}.{Body}");

        DocuSealWebhookSignature.IsValid(Secret, Body, $"{ts}.{signature}", Now).Should().BeFalse();
    }

    [Theory]
    [InlineData("1760875200.zz")]
    [InlineData("1760875200.abc")]
    [InlineData("notatime.abcd")]
    [InlineData("-1760875200.abcd")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Malformed_IsRejected(string? header)
        => DocuSealWebhookSignature.IsValid(Secret, Body, header, Now).Should().BeFalse();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LegacyBodyOnly_AnyCaseWithOrWithoutPrefix_IsValid(bool lower, bool prefixed)
    {
        var hex = Hex(Secret, Body);
        if (lower) hex = hex.ToLowerInvariant();
        var header = prefixed ? $"sha256={hex}" : hex;

        DocuSealWebhookSignature.IsValid(Secret, Body, header, Now).Should().BeTrue();
    }

    [Fact]
    public void NoSecretConfigured_IsRejected()
        => DocuSealWebhookSignature.IsValid("", Body, Hex("", Body), Now).Should().BeFalse();
}
