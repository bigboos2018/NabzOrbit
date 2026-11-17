# Nabz Orbit 2.1 · address provenance and protocol references

This is a bounded, embedded catalog, **not** a sweep of IP ranges. No executable or list is downloaded by the app. An address source establishes a candidate, not its identity on every route, uptime, privacy, game access or performance in Iran.

| Entry | Primary / alternate IPv4 (catalog order, not a speed rank) | Basis |
|---|---|---|
| Cloudflare | 1.1.1.1 / 1.0.0.1 | [1] standard |
| Google | 8.8.8.8 / 8.8.4.4 | [1] |
| Quad9 | 9.9.9.9 / 149.112.112.112 | [1] standard, threat filtering |
| OpenDNS | 208.67.222.222 / 208.67.220.220 | [1] standard |
| AdGuard | 94.140.14.140 / 94.140.14.141 | [1] non-filtering |
| Shecan | 178.22.122.100 / 185.51.200.2 | [2] free |
| Begzar | 185.55.226.26 / 185.55.225.25 | [3] first two addresses displayed |
| Control D | 76.76.2.0 / 76.76.10.0 | [1] non-filtering |
| DNS.SB | 185.222.222.222 / 45.11.45.11 | [1] |
| DNS4EU | 86.54.11.100 / 86.54.11.200 | [1] unfiltered |
| CZ.NIC ODVR | 193.17.47.1 / 185.43.135.1 | [1] |
| Yandex | 77.88.8.8 / 77.88.8.1 | [1] Basic |
| Radar Game **off by default** | 10.202.10.10 / 10.202.10.11 | [4] secondary reports, private addressing |
| 403.online **off by default** | 10.202.10.202 / 10.202.10.102 | [5] secondary reports, private addressing |

1. [AdGuard DNS knowledge base, GitHub revision 13387e9](https://github.com/AdguardTeam/KnowledgeBaseDNS/blob/13387e910fd2291e1ea369630e0764edd0f60772/docs/general/dns-providers.md). Ten entries checked against the named providers' IPv4 tables, using the pinned document bytes (SHA256 `f8bcfeb00d22c3f1c963c90d69cacc5e3b6eec03f56b2f39c8066cedf2124f95`). `python tools/verify_catalog.py` is a developer-only reproducibility check.
2. [Shecan Windows guide](https://shecan.ir/تحریم-شکن-برای-ویندوز/) and [operator information](https://shecan.ir/شکن-shecan/). Indexed operator material corroborates the pair; direct pages intermittently returned a database error. Not a live resolver test.
3. [Begzar operator instructions](https://begzar.ir/). The third displayed address is not used.
4. [Zoomit, Radar launch coverage](https://www.zoomit.ir/gaming/401287-radar-game-ping-cdn/) and [gaming DNS list](https://www.zoomit.ir/howto/423352-best-dns-gaming/). Secondary evidence; direct operator website was unavailable for confirmation here.
5. [Shabakehchi DNS guide](https://shabakehchi.com/education/28500/best-dns-for-bypassing-the-embargo) and [Zoomit](https://www.zoomit.ir/howto/423352-best-dns-gaming/). Secondary evidence; direct operator website was unavailable for confirmation here.

Only those four exact RFC1918 resolver addresses may be **new candidates**, and only with explicit in-app consent and route checks. Already configured, ordinary router/ISP resolver IPs may be measured as the read-only baseline. No resolver exception applies to **positive web destinations**. A private IP service cannot be globally authenticated by its numeric address or a route-table check. Do not enable this option on corporate, VPN, hotel, foreign or unknown networks.

## Diagnostic targets

[Steam](https://store.steampowered.com/about/), [Epic Games Store](https://store.epicgames.com/en-US/), [Xbox](https://www.xbox.com/en-US/), [Riot](https://www.riotgames.com/en), [Microsoft Learn](https://learn.microsoft.com/en-us/), [PyPI](https://pypi.org/project/pip/), and [IANA](https://www.iana.org/domains/reserved). Ordinary-web probes: [Example](https://example.com/) and the IANA page, excluding a duplicate target host. Web access is deliberately separate from DNS sample quality. These are **not** gameplay or authentication endpoints; e.g. [Epic documents multiple service domains](https://www.epicgames.com/help/c-202300000001619/c-202300000001672/which-domains-need-to-be-whitelisted-to-reach-the-epic-servers-a202300000012358).

## Platform and assets

- [RFC 1035](https://www.rfc-editor.org/rfc/rfc1035), [RFC 6761](https://www.rfc-editor.org/rfc/rfc6761); [Microsoft netsh interface documentation on GitHub](https://github.com/MicrosoftDocs/windowsserverdocs/blob/main/WindowsServerDocs/administration/windows-commands/netsh-interface.md); [Windows DNS flags/record sections](https://learn.microsoft.com/en-us/windows/win32/dns/dns-constants), [DnsQuery_W](https://learn.microsoft.com/en-us/windows/win32/api/windns/nf-windns-dnsquery_w), [DnsFree](https://learn.microsoft.com/en-us/windows/win32/api/windns/nf-windns-dnsfree), [source-specific ICMP](https://learn.microsoft.com/en-us/windows/win32/api/icmpapi/nf-icmpapi-icmpsendecho2ex).
- [Vazirmatn](https://github.com/rastikerdar/vazirmatn/tree/83629f877e8f084cc07b47030b5d3a0ff06c76ec) regular/bold by Saber Rastikerdar and contributors. Font bytes unchanged. `assets/OFL.txt` and `assets/FONT-SOURCE.txt` accompany them; loaded privately, not installed system-wide.
- Orbit's plum/apricot design, controls, graph and icon are retained from v2. The decorative planet art is AI-generated. The guide's labelled example screenshots are from the form with illustrative inputs, **not measured rankings**, and the shipping EXE starts with no sample scores.
