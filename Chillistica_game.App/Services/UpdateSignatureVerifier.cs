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
    // Release signing PUBLIC key (SubjectPublicKeyInfo, RSA-3072). The matching
    // private key exists only offline + as the CI secret CHILLISTICA_SIGNING_KEY_PEM.
    // To rotate: scripts/new-signing-key.ps1, replace this, update the secret.
    public const string PublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA3E5sjJ5NOXKnwEsp5kxw\n" +
        "wpBtrXmpxOsVS9xspFs9saNKK6SRBDe5k9C4U9PzR4RFM/Aj/0VySVQv5cbS8ZIE\n" +
        "40bPsHECptK+oWatR8eeaOiCmBL6e3SZNpleWkUjOtPR9sZ1gdLVsiebKdS3nsUg\n" +
        "V2l8r7Uopkx7b/Rx0QGzteaG7K4MwdMxV5YqfN5u3zaQ92WekDDPNbP49fHAp+IZ\n" +
        "d3p8wQKbj0C40Aabt3SZCL61aIyuKhhAhbHPkly80osm2Ho7FdDMzgzTtLjH+X94\n" +
        "iittE8rB0CVJNVbw0GMnXSst/OyNh5PDbuNTBMTHdrhWMpZsTedlbMmJdWT1iIfG\n" +
        "edAHzDCoO/rAB+k1USBJM59do43FT8bl+1viHkd9cBu44ZNT+e9M3mxn0YcAAd+V\n" +
        "IoKZOW+CIbBptyQTFxT1rDcy2FDeCHETGxNpf/DYfhedNpa1kVvbH4Q/wlRGD5/K\n" +
        "ccjuNL2p6yZTk76oHdAHkTGTv+NEu2YY+SXdNU68PTzRAgMBAAE=\n" +
        "-----END PUBLIC KEY-----";

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
