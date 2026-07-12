using System.IO;
using System.Security.Cryptography;

namespace Chillistica_game.App.Services;

/// <summary>
/// Verifies the authenticity of a downloaded update package against a
/// compiled-in (pinned) RSA public key. This is the trust anchor for the
/// auto-update channel: an update is only ever applied if its detached
/// signature validates against this key, so a compromised/spoofed GitHub
/// release or a TLS-intercepting proxy cannot deliver an executable payload.
///
/// The private key is kept OFFLINE (never on the build agent beyond a CI
/// secret used only to sign the release zip). See docs/UPDATE_SIGNING.md.
///
/// SECURITY: <see cref="PublicKeyPem"/> is empty by default, so verification
/// fails closed — auto-update apply is DISABLED until a real key is pinned.
/// </summary>
public static class UpdateSignatureVerifier
{
    // Paste the release signing PUBLIC key here (SubjectPublicKeyInfo PEM,
    // "-----BEGIN PUBLIC KEY-----"). Generate with scripts/new-signing-key.ps1.
    // Leave empty to keep auto-update disabled (fail closed).
    public const string PublicKeyPem = "";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKeyPem);

    /// <summary>
    /// Returns true only if <paramref name="signatureBytes"/> is a valid
    /// RSA-SHA256 signature over the contents of <paramref name="filePath"/>
    /// under the pinned public key. Fails closed on any error or if unset.
    /// </summary>
    public static bool VerifyFile(
        string filePath,
        byte[] signatureBytes)
    {
        if (!IsConfigured ||
            signatureBytes is null ||
            signatureBytes.Length == 0 ||
            !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);

            using FileStream stream =
                File.OpenRead(filePath);

            return rsa.VerifyData(
                stream,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
}
