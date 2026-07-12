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

## License note (reviewed 2026-07-12)

The upstream repo `itdoginfo/allow-domains` has no explicit LICENSE. We keep it
as the automatic per-app hostlist source anyway, as a deliberate, low-risk call
for a free/non-commercial anti-censorship tool:

- The data is **factual** — the set of domain names a given service uses. Short
  factual lists carry little to no copyright, and `scripts/update-hostlists.ps1`
  further normalizes them (lowercase, de-dup, ordinal sort), so we ship a
  normalized factual set, not the upstream project's arrangement.
- The DPI engine we ship (bol-van's zapret / winws) is itself distributed
  without a license; this project follows the same ecosystem norm.
- Attribution is given here in good faith.

If you ever want maximal license certainty, either:
1. Ask the itdoginfo maintainer to add a permissive license (MIT/CC0), or
2. Point `-BaseUrl` in `scripts/update-hostlists.ps1` at an MIT-licensed source
   such as `1andrevich/Re-filter-lists` — note it is aggregate (one `domains_all`
   list), not per-service, so the per-app selectivity would be coarser.

