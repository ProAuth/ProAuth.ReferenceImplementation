using System.Text;
using System.Text.Json;
using Common.Evidence;
using Xunit;
namespace Common.Evidence.Tests;

public class ProtocolEvidenceTests
{
    static string Jwt(object body) => "eyJhbGciOiJSUzI1NiJ9." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(body)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";
    [Fact]
    public void TokenProjectionOmitsCredentialsAndUnselectedClaims()
    {
        var raw = Jwt(new
        {
            iss = "https://idp.localtest.me",
            aud = "urn:test",
            sub = "person@example.org",
            email = "secret@example.org",
            scope = "quote",
            cnf = new
            {
                jkt = "key-thumbprint"
            },
            exp = 123
        });
        var summary = EvidenceProjection.Token(raw);
        var json = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain(raw, json);
        Assert.DoesNotContain("person@example.org", json);
        Assert.DoesNotContain("secret@example.org", json);
        Assert.Equal("urn:test", summary.Audience);
        Assert.Equal("key-thumbprint", summary.Jkt);
        Assert.NotNull(summary.SubjectFingerprint);
    }
    [Fact]
    public void OpaqueTokenIsNeverInventedAsClaims()
    {
        var summary = EvidenceProjection.Token("opaque-secret-value");
        Assert.Null(summary.Audience);
        Assert.Null(summary.Jkt);
        Assert.NotEmpty(summary.Fingerprint);
    }
    [Fact]
    public void EvidenceRejectsUnknownSourceAndRawCompactCredential()
    {
        var good = new ProtocolObservation(Guid.NewGuid(), DateTimeOffset.UtcNow, "Attacker", "replay", 401, "observed");
        Assert.True(good.IsValid());
        Assert.False((good with
        {
            Source = "arbitrary"
        }).IsValid());
        Assert.False((good with
        {
            Challenge = new string('a', 20) + "." + new string('b', 20) + ".signature"
        }).IsValid());
        Assert.False((good with
        {
            Error = "remote secret response"
        }).IsValid());
    }
    [Fact]
    public void ProofProjectionDoesNotExportSignatureOrNonce()
    {
        var proof = Jwt(new
        {
            htm = "GET",
            htu = "https://api.localtest.me/api/quote",
            ath = "tokenhash",
            jti = "unique-id",
            nonce = "server-secret"
        });
        var summary = EvidenceProjection.Proof(proof)!;
        Assert.True(summary.NoncePresent);
        Assert.Equal("GET", summary.Method);
        Assert.DoesNotContain("server-secret", JsonSerializer.Serialize(summary));
        Assert.DoesNotContain("unique-id", JsonSerializer.Serialize(summary));
    }
    [Fact]
    public void UnknownFieldsAreRejectedAtIngestion()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProtocolObservation>("{\"access_token\":\"secret\"}"));
    }
}
