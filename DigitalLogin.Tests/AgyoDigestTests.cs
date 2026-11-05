using DigitalLogin.Auth;
using Xunit;

namespace DigitalLogin.Tests;

public class AgyoDigestTests
{
    [Fact]
    public void Sha256Hex_matches_known_vector()
    {
        // SHA-256("abc"), the standard FIPS 180-2 test vector.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            AgyoDigest.Sha256Hex("abc"));
    }

    [Fact]
    public void Compute_follows_the_bruno_formula()
    {
        const string id = "Company-1";
        const string secret = "S3cret";
        const string nonce = "nonce-value";

        var inner = AgyoDigest.Sha256Hex("company-1" + "s3cret");
        var expected = AgyoDigest.Sha256Hex(inner + nonce);

        Assert.Equal(expected, AgyoDigest.Compute(id, secret, nonce));
    }

    [Fact]
    public void Compute_matches_value_produced_by_sha256sum()
    {
        // Pinned from an independent run of:
        //   printf '%s' "$(printf '%s' 'Company-1S3cret' | tr A-Z a-z)" | sha256sum   -> inner
        //   printf '%s' "${inner}nonce-value" | sha256sum                             -> digest
        Assert.Equal("2985dd904ee65d07b869ee2a8d4cc06d4486de61d7d6dcdeb9454902691d9a2d",
            AgyoDigest.Compute("Company-1", "S3cret", "nonce-value"));
    }

    [Fact]
    public void Compute_lowercases_id_and_secret_before_hashing()
    {
        Assert.Equal(
            AgyoDigest.Compute("abc", "secret", "n1"),
            AgyoDigest.Compute("ABC", "SECRET", "n1"));
    }

    [Fact]
    public void Compute_changes_with_the_nonce()
    {
        Assert.NotEqual(
            AgyoDigest.Compute("id", "secret", "n1"),
            AgyoDigest.Compute("id", "secret", "n2"));
    }

    [Fact]
    public void Compute_returns_64_lowercase_hex_characters()
    {
        var digest = AgyoDigest.Compute("id", "secret", "nonce");

        Assert.Equal(64, digest.Length);
        Assert.Matches("^[0-9a-f]{64}$", digest);
    }
}
