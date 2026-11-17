"""Build an offline portable bundle; only the explicit source/resource trees are packaged."""
import hashlib
import sys
import zipfile
from pathlib import Path


def main():
    if len(sys.argv) != 3:
        raise SystemExit('Usage: python tools/package.py release/NabzOrbit.exe release/NabzOrbit-2.1-Windows.zip')
    root = Path(__file__).resolve().parents[1]
    exe, dest = map(Path, sys.argv[1:])
    guide = root / 'Start-here.html'
    if not guide.is_file():  # source as extracted from the portable archive
        guide = root.parent / 'Start-here.html'
    files = {'NabzOrbit.exe': exe, 'Start-here.html': guide}
    for name in ('.editorconfig', '.gitignore', 'LICENSE', 'README.md', 'build.cmd', 'test.cmd'):
        files['Source/' + name] = root / name
    for directory in ('.github', 'src', 'tests', 'assets', 'docs', 'tools'):
        for p in sorted((root / directory).rglob('*')):
            if p.is_file() and '__pycache__' not in p.parts and not p.name.endswith(('.orig', '.pyc', '~')):
                files['Source/' + p.relative_to(root).as_posix()] = p
    # Include guide in source so extracted Source/ is independently ready to push/build/package.
    files['Source/Start-here.html'] = guide
    manifest = ''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + name + '\n' for name,p in files.items())
    dest.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(dest, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, p in files.items():
            z.write(p, name)
        z.writestr('SHA256.txt', manifest)
    with zipfile.ZipFile(dest) as z:
        assert z.testzip() is None and len(z.namelist()) == len(files) + 1
        for line in z.read('SHA256.txt').decode().splitlines():
            digest, name = line.split('  ', 1)
            assert hashlib.sha256(z.read(name)).hexdigest() == digest
    print(str(dest), '(' + str(len(files) + 1) + ' entries), SHA256:', hashlib.sha256(dest.read_bytes()).hexdigest())


if __name__ == '__main__':
    main()
