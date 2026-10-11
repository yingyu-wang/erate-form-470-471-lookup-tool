#!/usr/bin/env python3
"""Package published Avalonia binaries into a macOS .app and preserve Unix modes."""
from pathlib import Path
import plistlib
import shutil
import stat
import sys
import zipfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
rid = sys.argv[1]
if rid not in ('osx-arm64', 'osx-x64'):
    raise SystemExit('Use osx-arm64 or osx-x64')
output = root / 'publish' / rid
binary_dir = output / 'bin'
if not (binary_dir / 'ERateLookup').is_file():
    raise SystemExit('Publish the MacDesktop project first.')
bundle = output / 'ERateLookup.app'
if bundle.exists():
    shutil.rmtree(bundle)
contents = bundle / 'Contents'
shutil.copytree(binary_dir, contents / 'MacOS')
(contents / 'Resources').mkdir()
shutil.copy2(root / 'Assets' / 'ERateLookup.icns', contents / 'Resources' / 'ERateLookup.icns')
executable = contents / 'MacOS' / 'ERateLookup'
executable.chmod(0o755)
for dylib in (contents / 'MacOS').glob('*.dylib'):
    dylib.chmod(0o755)
info = {
    'CFBundleIconFile': 'ERateLookup.icns',
    'CFBundleName': 'E-rate Lookup',
    'CFBundleDisplayName': 'E-rate Form 470 / 471 Lookup',
    'CFBundleExecutable': 'ERateLookup',
    'CFBundleIdentifier': 'org.eratelookup.desktop',
    'CFBundlePackageType': 'APPL',
    'CFBundleShortVersionString': ET.parse(root / 'MacDesktop' / 'MacDesktop.csproj').findtext('.//Version') or '1.0.1',
    'CFBundleVersion': ET.parse(root / 'MacDesktop' / 'MacDesktop.csproj').findtext('.//Version') or '1.0.1',
    'LSMinimumSystemVersion': '14.0',
    'NSHighResolutionCapable': True,
}
with (contents / 'Info.plist').open('wb') as stream:
    plistlib.dump(info, stream)
version = ET.parse(root / 'MacDesktop' / 'MacDesktop.csproj').findtext('.//Version') or '1.0.1'
architecture = 'arm64' if rid == 'osx-arm64' else 'x64'
archive = root.parent / f'ERateLookup-v{version}-macos-{architecture}.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for path in sorted(bundle.rglob('*')):
        relative = Path(bundle.name) / path.relative_to(bundle)
        entry = zipfile.ZipInfo(str(relative) + ('/' if path.is_dir() else ''))
        entry.create_system = 3
        entry.compress_type = zipfile.ZIP_DEFLATED
        entry.external_attr = (path.stat().st_mode & 0xffff) << 16
        if path.is_dir():
            entry.external_attr |= 0x10
        z.writestr(entry, b'' if path.is_dir() else path.read_bytes())
    for name in ('MACOS_README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'RELEASE_NOTES.md'):
        z.write(root / name, name)
print(f'{archive}: {archive.stat().st_size:,} bytes')
