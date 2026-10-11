#!/usr/bin/env python3
"""Install a per-user launcher for this extracted portable app (no sudo needed)."""
from pathlib import Path
import os

root = Path(__file__).resolve().parent
if not (root / 'ERateLookup').is_file():
    raise SystemExit('Run this script from the extracted Linux package.')
# Desktop Entry quoting differs from shell quoting. Exec also treats % as a field code.
def desktop_quote(value, executable=False):
    value = str(value)
    if any(c in value for c in ('\n', '\r', '\x00')):
        raise SystemExit('The application path cannot contain line breaks.')
    value = value.replace(chr(92), chr(92) * 4).replace('"', chr(92) * 2 + '"').replace('`', chr(92) * 2 + '`').replace('$', chr(92) * 2 + '$')
    if executable:
        value = value.replace('%', '%%')
    return '"' + value + '"'

base = Path(os.environ.get('XDG_DATA_HOME') or Path.home() / '.local/share')
if not base.is_absolute():
    raise SystemExit('XDG_DATA_HOME must be an absolute path.')
launcher = base / 'applications/org.eratelookup.desktop.desktop'
launcher.parent.mkdir(parents=True, exist_ok=True)
# Icon is a string value, not an Exec token: desktop string escaping only.
icon = str(root / 'ERateLookup.png').replace('\\', '\\\\')
launcher.write_text('[Desktop Entry]\nType=Application\nName=E-rate Lookup\n'
                    'Comment=Find published E-rate Form 470 and 471 filings\n'
                    f'Exec={desktop_quote(root / "ERateLookup", executable=True)}\n'
                    f'Icon={icon}\nTerminal=false\nCategories=Office;Utility;\n', encoding='utf-8')
launcher.chmod(0o644)
print(f'Created {launcher}. Keep the app at {root}; rerun this script after moving it.')
