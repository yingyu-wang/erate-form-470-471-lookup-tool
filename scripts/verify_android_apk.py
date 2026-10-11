"""Verify standalone preview packaging; this does not assert device runtime behavior."""
import argparse
import struct
import subprocess
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("apk")
parser.add_argument("--aapt", required=True)
parser.add_argument("--apksigner", required=True)
args = parser.parse_args()
subprocess.run([args.apksigner, "verify", "--verbose", "--print-certs", args.apk], check=True)
badging = subprocess.check_output([args.aapt, "dump", "badging", args.apk], text=True)
print(badging)
assert "name='com.yingyuwang.eratelookup.android'" in badging
assert "versionCode='110'" in badging and "versionName='1.1.0'" in badging
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
