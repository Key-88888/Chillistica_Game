# Update signing (required before the update check works)

The update channel is **fail-closed**: the app refuses to offer an update unless
the downloaded package carries a valid RSA-SHA256 signature that verifies against
a **public key compiled into the app**. That blocks the supply-chain / MITM risk
— a spoofed or compromised release cannot get the user to install an attacker's
payload, because the app tells them the package failed verification.

Until you complete the one-time setup below, `UpdateSignatureVerifier.PublicKeyPem`
is empty and the update check stays disabled (this is intentional and safe).

Since the service-less rebuild the app is distributed **unzip-and-run**, so there
is no installer and no elevated apply step. See "How verification flows" for what
that means for the trust model.

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
   `chillistica-signing-private.pem`. The release workflow signs both zips with
   it and uploads the matching `.sig` assets.

   `build-release.ps1` **refuses to build a tagged (`v*`) release when this
   secret is missing**, so a release can no longer be published unsigned — an
   unsigned release would silently disable the update check for everyone who
   installs it.

## How verification flows

- `build-release.ps1` signs both zips → `.sig` assets (CI, or locally via
  `scripts/sign-release.ps1`).
- Client `UpdateCheckService.CheckForUpdateAsync` only offers an update when a
  key is pinned, and only for assets served over HTTPS from `github.com` /
  `*.githubusercontent.com`, with the asset name bound to the advertised tag
  (rollback defence).
- Pressing **Обновить** downloads the zip **and** its `.sig`, verifies the
  signature against the pinned public key, and only then reveals the file in
  Explorer. A package that fails verification is deleted and never shown as
  trusted; the user is told plainly that a manual download from the releases
  page is *not* signature-checked.
- **There is no automatic apply step.** "Installing" is the user unzipping over
  their folder and starting the app again. The value the signing chain adds is
  that the bytes were checked against the pinned key *before* the user is
  pointed at them.

Note the consequence for the threat model: because the app is unzipped into a
user-writable folder, a local attacker who can write that folder does not need
to attack the update channel at all. That path is covered separately by the
engine trust gate (`EngineIntegrity`: pinned hashes plus sealed directories),
not by update signing.

## Key rotation

Rotate by generating a new keypair, pinning the new public key, and shipping a
release. Clients on the old build verify against the old key until they update;
publish the last old-key-signed release as the migration bridge.

## Recommended additional hardening (not code)

- Authenticode-sign `Chillistica_game.exe` so the UAC prompt shows a real
  publisher, and so the OS enforces binary integrity independently of our own
  checks. This matters more than usual here: the app self-elevates, and its own
  integrity is the root of the engine trust gate.
