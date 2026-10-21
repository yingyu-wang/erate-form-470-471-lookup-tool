"""Verify standalone preview packaging; this does not assert device runtime behavior."""
import argparse
from pathlib import Path
import struct
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("apk")
parser.add_argument("--badging-file", required=True, type=Path,
                    help="UTF-8 output from the CI aapt dump badging step")
args = parser.parse_args()
# External SDK tools are invoked by CI, never from command-line executable paths.
# Signature verification must succeed there before this data-only check runs.
badging = args.badging_file.read_text(encoding="utf-8")
print(badging)
assert "name='com.yingyuwang.eratelookup.android'" in badging
assert "versionCode='115'" in badging and "versionName='1.0.6'" in badging
assert "sdkVersion:'26'" in badging
assert "launchable-activity: name='com.yingyuwang.eratelookup.android.MainActivity'" in badging
assert "android.permission.INTERNET" in badging
assert "android.permission.MANAGE_EXTERNAL_STORAGE" not in badging
assert "android.permission.WRITE_EXTERNAL_STORAGE" not in badging
assert "application-icon" in badging
with zipfile.ZipFile(args.apk) as apk:
    assert apk.testzip() is None
    assert "classes.dex" in apk.namelist()
    for abi, machine in [("arm64-v8a", 183), ("x86_64", 62)]:
        sqlite = apk.read(f"lib/{abi}/libe_sqlite3.so")
        assert sqlite[:4] == b"\x7fELF"
        endian = "<" if sqlite[5] == 1 else ">"
        assert struct.unpack_from(endian + "H", sqlite, 18)[0] == machine
        print(f"Verified {abi} native SQLite")
    assert "assets/LICENSE.txt" in apk.namelist()
    assert "assets/THIRD_PARTY_NOTICES.md" in apk.namelist()
print("APK packaging checks passed. Device functionality remains a separate test.")
