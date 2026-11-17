"""Developer check only; the desktop app never downloads a resolver list."""
from pathlib import Path
import argparse
import hashlib
import re
from urllib.request import Request, urlopen

COMMIT = '13387e910fd2291e1ea369630e0764edd0f60772'
BASE = 'https://raw.githubusercontent.com/AdguardTeam/KnowledgeBaseDNS/'
URL = BASE + COMMIT + '/docs/general/dns-providers.md'
ROOT = Path(__file__).resolve().parent.parent
SECTIONS = {'cloudflare': 'Cloudflare DNS', 'google': 'Google DNS',
            'quad9': 'Quad9 DNS', 'opendns': 'Cisco OpenDNS', 'adguard': 'AdGuard DNS', 'controld': 'ControlD', 'dnssb': 'DNS.SB', 'dns4eu': 'DNS4EU', 'cznic': 'CZ.NIC ODVR', 'yandex': 'Yandex DNS'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, help='Verify against a local copy of the pinned upstream file')
    args = parser.parse_args()
    if args.upstream:
        content = args.upstream.read_bytes()
    else:
        with urlopen(Request(URL, headers={'User-Agent': 'NabzDNS-catalog-check/1'}), timeout=15) as reply:
            content = reply.read()
    assert hashlib.sha256(content).hexdigest() == "f8bcfeb00d22c3f1c963c90d69cacc5e3b6eec03f56b2f39c8066cedf2124f95", "upstream content differs from the pinned bytes"
    current = ''
    pairs = set()
    for line in content.decode('utf-8').splitlines():
        if line.startswith('### '):
            current = line[4:].strip()
        if re.match(r'^\| DNS, IPv4\s*\|', line):
            ip = re.findall(r'`([0-9.]+)`', line.split('|')[2])
            if len(ip) == 2:
                pairs.add((current, *ip))
    rows = (ROOT / 'assets/catalog.tsv').read_text(encoding='utf-8-sig').splitlines()
    regional = {'shecan': ('178.22.122.100', '185.51.200.2'), 'begzar': ('185.55.226.26', '185.55.225.25'), 'radar': ('10.202.10.10','10.202.10.11'), 'ir403': ('10.202.10.202','10.202.10.102')}
    if len(rows) != len(SECTIONS) + len(regional):
        raise SystemExit('Unexpected number of providers')
    for row in rows:
        tag, name, a, b, _ = row.split('\t')
        if tag in regional:
            if (a, b) != regional[tag]:
                raise SystemExit(f'Unexpected regional pair: {name}')
            print(f'OK (regional snapshot; see docs/SOURCES.md): {name}: {a}, {b}')
            continue
        if (SECTIONS[tag], a, b) not in pairs:
            raise SystemExit(f'MISMATCH: {name}: {a}, {b}')
        print(f'OK: {name}: {a}, {b}')
    print(f'Upstream commit: {COMMIT}\nContent SHA256: {hashlib.sha256(content).hexdigest()}')


if __name__ == '__main__':
    main()
