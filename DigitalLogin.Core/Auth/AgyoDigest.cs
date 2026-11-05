using System.Security.Cryptography;
using System.Text;

namespace DigitalLogin.Auth;

/// <summary>
/// The digest the token endpoint expects. Same arithmetic as the Bruno pre-request script:
///
///   digest = SHA256( SHA256( lower(id) + lower(secret) ) + nonce )
///
/// Both hashes are lowercase hex over the UTF-8 bytes of the string. The inner hex string is
/// concatenated with the nonce as text, not as bytes.
/// </summary>
public static class AgyoDigest
{
    public static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static string Compute(string id, string secret, string nonce) =>
        Sha256Hex(Sha256Hex(id.ToLowerInvariant() + secret.ToLowerInvariant()) + nonce);
}
