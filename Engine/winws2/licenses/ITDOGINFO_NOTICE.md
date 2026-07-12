# Hostlist source notice

The per-app winws hostlists (`Engine/winws2/files/list-youtube.txt`,
`list-discord.txt`, `list-roblox.txt`) are re-vendored automatically from the
maintained upstream domain-list project:

- **itdoginfo/allow-domains** — https://github.com/itdoginfo/allow-domains
  (`Services/youtube.lst`, `Services/discord.lst`, `Services/roblox.lst`).

Re-vendoring is done by `scripts/update-hostlists.ps1` and delivered to users
only through the signed release channel (the lists are SHA256-pinned in the
strategy/profile JSONs). `list-fortnite.txt` has no upstream equivalent there and
is maintained by hand.

TODO before public distribution: confirm the upstream project's license permits
redistribution of these domain lists and add its license text/terms here.
