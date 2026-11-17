# Release verification · Nabz Orbit 2.1

## What ran locally

See `TEST-RUN.txt` for the exact results and the hash of the tested EXE. Microsoft C# compiler (Roslyn 1.3.2), C# 5, official .NET Framework 4.6 reference assemblies, AnyCPU, GUI subsystem, embedded `asInvoker` manifest, icon and app resources. Executed under Linux/Mono on a headless X11 desktop (Xvfb). Do not confuse this with running Windows.

- Controlled five-round catalog + baseline measurements, no duplicate queries for shared addresses, destination/ordinary-web isolation even on the same CDN IP, rate/step bounds, private answer vetoes, fallback general site, ranking, normalized one-address reference vs pair, conservative keep-current threshold, changed-link and expiry handling.
- Actual loopback DNS over UDP including timeout/cancel and a matched truncation followed by TCP to the **same** endpoint. Native DNS_RECORDW layout/parser tests on process-owned synthetic records, including CNAME, unrelated sections and mixed private/public answers. This is not a call to the live Windows DNS API on Linux.
- Fake adapter/registry/netsh state: pending journal before mutation, settings readback, rollback to ordered manual or DHCP, cancelled/expired/unhealthy trials, failed switch back to previous pair, first baseline survives, external edit/changed-token/crash/failed-rollback paths. No real NIC is modified.
- Trial checks require both resolvers, target and all previously functioning ordinary sites (and at least one regular site). Actual confirmation form under Xvfb: deadline expires with a slow fake probe, and keep becomes possible only after two consecutive successful fake cycles. Time-bound decision logic and recovery are tested separately from real Windows network integration.
- Main window initializes with 12 unmeasured rows, opt-in off, and scroll access to the last row. Diagnostics include correlated gateway/TCP/DNS cases, incomplete data, ISP/route suspicion vs DNS suspicion, and CSV with missing values and event markers. Separate `tools/SmokeRelease.cs` inspects the **shipping executable**, manifest and resources, opens its window and closes it again.
- Real loopback TLS fixture (`python3 tests/loopback_tls.py /path/to/Nabz.Tests.exe`): trusted correct-name HEAD 200, 403 not a pass, 302 not followed, wrong-name certificate rejected. Only the throwaway test user's temporary Mono trust store receives the fixture CA. No production certificate override.

## What did not run

No measured ranking from the recipient's link or an Iranian ISP. No live Windows UAC, IP_UNICAST_IF/ICMP/DnsQuery_W or adapter mutation on Windows 10/11 in this environment. No login, matchmaking, long-duration resilience or in-game UDP test. DNS readback and a recovered settings mode are not guarantees of restored ISP connectivity. GitHub Actions is supplied for reproducibility, not claimed to have run remotely.

## Windows validation on a personal, non-critical connection

1. Record adapter and original IPv4 DNS mode/order first; observe IPv6 settings as a non-modification control. `Get-DnsClientServerAddress -AddressFamily IPv4` is read-only; adapter properties distinguish automatic from an identical static list. Keep a copy of this offline guide. Do not perform mutation tests on a managed or critical connection.
2. Run `build.cmd`, `test.cmd --ui`, and `tools\smoke.cmd` without elevation. Windows-only inventory test reads (never writes) the local adapter list and existing app journal. The Windows GUI smoke suppresses startup traffic.
3. On the real desktop, launch normally, select the right interface and service, cancel once and confirm DNS did not change. Switch profile and ensure old results can't be applied. Scroll to the bottom and inspect the current reference. Do not enable private candidates on an unknown route.
4. If a candidate qualifies and you want to trial it, decline UAC first: nothing should change. On the next attempt, approve UAC and let the trial **expire**. Check that the previous DNS mode/order returns. In a different attempt, actively choose Undo. Only choose Keep after browsing and opening your service successfully. Check the two IPv4 DNS addresses after confirmation, then use Disconnect and check the original mode/order again.
5. If appropriate, test switching away from an already active app-owned pair and cancelling the trial; the previous pair should come back, while Disconnect should restore the **first** baseline. Repeat with an initially manual list and DHCP. Reopening the app should recognize a v1/v2 journal. Never destroy a journal to simulate a clean run.
6. Record a Match Watch session and mark an interruption. Export the local CSV before closing. One blocked control site, absent IPv4 resolver or silent ICMP gateway does not diagnose a whole-ISP outage. Browser secure DNS/IPv6 and a running game's existing socket can take different paths.
