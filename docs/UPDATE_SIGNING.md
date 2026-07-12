# Update signing (required before auto-update works)

The auto-update channel is **fail-closed**: the app refuses to offer or apply any
update unless the downloaded package carries a valid RSA-SHA256 signature that
verifies against a **public key compiled into the app**. This blocks the
supply-chain / MITM remote-code-execution risk (a spoofed or compromised release
cannot deliver an executable payload) and, together with the trusted installed
`apply-update.ps1`, the local user-writable-staging escalation.

Until you complete the one-time setup below, `UpdateSignatureVerifier.PublicKeyPem`
is empty and auto-update stays disabled (this is intentional and safe).

## One-time setup

1. **Generate an offline keypair** (PowerShell 7 / `pwsh`):

   ```pwsh
   pwsh ./scripts/new-signing-key.ps1 -OutDir .
   ```

   This writes `chillistica-signing-private.pem` (KEEP OFFLINE, never commit) and
   `chillistica-signing-public.pem`, and prints the public key.

2. **Pin the public key** into the app: paste the printed `-----BEGIN PUBLIC KEY-----`
   block into `Chillistica_game.App/Services/UpdateSignatureVerifier.cs`
   (`PublicKeyPem` constant). Commit that change and cut a new release so clients
   ship with the key.

3. **Add the private key as a CI secret**: repository *Settings → Secrets and
   variables → Actions →* `CHILLISTICA_SIGNING_KEY_PEM` = the full contents of
   `chillistica-signing-private.pem`. The release workflow signs the zip with it
   and uploads `...-win-x64.zip.sig` alongside the package.

## How verification flows

- `build-release.ps1` signs the zip → `.sig` asset (CI, or locally via
  `scripts/sign-release.ps1`).
- Client `UpdateCheckService` downloads the zip + `.sig` only over HTTPS from
  `github.com` / `*.githubusercontent.com`, and verifies the signature with the
  pinned public key **before** anything is extracted or elevated.
- The app launches the **installed, admin-only** `apply-update.ps1` (never the
  downloaded copy). That script re-verifies the signature by invoking the
  installed, signed `Chillistica_game.App.exe --verify-update <zip> <sig>` before
  replacing any files, closing the time-of-check/time-of-use window.

## Key rotation

Rotate by generating a new keypair, pinning the new public key, and shipping a
release. Clients on the old build verify against the old key until they update;
publish the last old-key-signed release as the migration bridge.

## Recommended additional hardening (not code)

- Authenticode-sign `Chillistica_game.App.exe`, `Chillistica_game.Service.exe`
  and the installer so the UAC prompt shows a real publisher instead of
  "Windows PowerShell", and so the OS enforces binary integrity independently.
