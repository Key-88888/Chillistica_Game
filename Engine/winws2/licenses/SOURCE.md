# winws2 bundle source

Source repository:

https://github.com/bol-van/zapret-win-bundle

Pinned commit:

83edb1d5f910ec88a48ee11b1be9fccb3a2296a7

Imported source directory:

zapret-winws

Imported files:

- winws2.exe
- WinDivert.dll
- WinDivert64.sys

Target platform:

- Windows 10 x64
- Windows 11 x64

Files from arm64, win7 and windivert-hide were not imported.

The files must not be executed until:

- the profile contains verified SHA256 entries;
- AllowUnsafeStart remains false during preflight;
- the command line is reviewed;
- network rollback is implemented and tested.
