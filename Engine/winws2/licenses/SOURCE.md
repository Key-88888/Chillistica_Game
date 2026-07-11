# winws (zapret v1) bundle source

Source repository:

https://github.com/bol-van/zapret-win-bundle

Pinned commit:

83edb1d5f910ec88a48ee11b1be9fccb3a2296a7

Imported source directory:

zapret-winws

Imported files:

- winws.exe            (zapret v1 engine, github version v72.12)
- cygwin1.dll          (cygwin runtime that winws.exe links against)
- WinDivert.dll        (zapret-win-bundle matched build)
- WinDivert64.sys
- files/quic_initial_www_google_com.bin (QUIC Initial fake payload)

Target platform:

- Windows 10 x64
- Windows 11 x64

Files from arm64, win7 and windivert-hide were not imported.

## 2026-07-12: migrated engine winws2 -> mature winws (zapret v1)

The experimental `winws2.exe` (bol-van's lua-based *zapret2* rewrite) crashed
immediately with STATUS_ACCESS_VIOLATION (0xC0000005) on start on Windows 11
build 26200, even elevated and with no arguments. The mature, mass-deployed
`winws.exe` (zapret v1, the engine inside the popular zapret-discord-youtube
bundles) was verified to run correctly and start WinDivert packet capture on
the same build. The engine binary was therefore swapped winws2.exe ->
winws.exe, and `cygwin1.dll` (a required runtime dependency that had been
missing) was added.

Strategy definitions were rewritten from winws2's lua option syntax
(`--lua-init`/`--lua-desync`/`--payload`/`--out-range`) to winws1's native
option syntax (`--dpi-desync=...`, `--dpi-desync-fake-tls-mod=...`,
`--filter-l7=...`), following `zapret-winws/preset1_example.cmd`.

Note: the `Engine\winws2\` directory name is retained as-is for historical /
compatibility reasons even though it now hosts the winws (zapret v1) engine.

## 2026-07-12: WinDivert.dll reverted to the zapret-win-bundle matched build

A previous (2026-07-09) change had swapped in the official basil00/WinDivert
v2.2.2 `WinDivert.dll` (SHA256 C1E060...) on the theory that the bundled DLL
was the cause of the winws2 crash. That was incorrect: winws2 crashed for its
own reasons, and zapret ships its own matched `WinDivert.dll` (SHA256
06C3F2...) paired with its winws builds. `WinDivert.dll` is now the
zapret-win-bundle matched build again, which is what the verified-working
`winws.exe` expects. `WinDivert64.sys` is byte-for-byte identical between
zapret-win-bundle and official basil00 v2.2.2 and did not change.

The files must not be executed until:

- the profile contains verified SHA256 entries;
- the resolved executable path matches `Engine\winws2\trusted-manifest.json` (pinned, installer-controlled — see `EngineTrustManifestLoader`);
- the command line is reviewed;
- network rollback is implemented and tested.
