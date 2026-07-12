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

## LICENSE STATUS: UNRESOLVED (blocker for public distribution)

As of 2026-07-12 the upstream repo `itdoginfo/allow-domains` has **no LICENSE
file and no license metadata** (GitHub reports no license; only a README). Under
default copyright that means "all rights reserved", so **redistributing these
domain lists inside the release package is not clearly permitted**.

- OK for personal/own use (running the tool yourself).
- NOT cleared for bundling into a publicly distributed release.

Resolve before public distribution, by ONE of:
1. Ask the upstream maintainer to add a permissive license (MIT/CC0/etc.), or
   obtain written permission to redistribute.
2. Switch the hostlist source in `scripts/update-hostlists.ps1` to a
   clearly-licensed project (e.g. verify `1andrevich/Re-filter-lists`), or
3. Ship only hand-maintained lists (drop the auto-vendoring of these three).

