#!/usr/bin/env python3
"""Bundle the full self-contained Linux publish tree, with Unix executable modes."""
from pathlib import Path
import shutil
import sys
import tarfile
import re

root = Path(__file__).resolve().parents[1]
if len(sys.argv) != 2 or sys.argv[1] not in ('linux-x64', 'linux-arm64'):
    raise SystemExit('Use linux-x64 or linux-arm64')
rid = sys.argv[1]
# Read the literal project version without interpreting XML entities or DTDs.
versions = re.findall(r'<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>',
                      (root / 'LinuxDesktop/ERateLookup.Linux.csproj').read_text())
if len(versions) != 1:
    raise SystemExit('Expected one literal numeric Linux package version')
version = versions[0]
output = root / 'publish' / rid
binary_dir = output / 'bin'
for required in ('ERateLookup', 'libcoreclr.so', 'libSkiaSharp.so', 'libe_sqlite3.so'):
    if not (binary_dir / required).is_file():
        raise SystemExit(f'Missing self-contained publish file: {required}')
name = f'ERateLookup-v{version}-{rid}'
package = output / name
if package.exists():
    shutil.rmtree(package)
shutil.copytree(binary_dir, package)
(package / 'ERateLookup').chmod(0o755)
for source, target in [('docs/LINUX_INSTALL.md', 'LINUX_INSTALL.md'),
                       ('LICENSE', 'LICENSE'), ('CONTRIBUTORS.md', 'CONTRIBUTORS.md'), ('THIRD_PARTY_NOTICES.md', 'THIRD_PARTY_NOTICES.md'),
                       ('Assets/ERateLookup.png', 'ERateLookup.png'),
                       ('LinuxDesktop/install-shortcut.py', 'install-shortcut.py')]:
    shutil.copy2(root / source, package / target)
(package / 'install-shortcut.py').chmod(0o755)
archive_dir = root / 'artifacts'
archive_dir.mkdir(exist_ok=True)
archive = archive_dir / f'{name}.tar.gz'
with tarfile.open(archive, 'w:gz') as stream:
    stream.add(package, arcname=name)
print(f'{archive}: {archive.stat().st_size:,} bytes')
