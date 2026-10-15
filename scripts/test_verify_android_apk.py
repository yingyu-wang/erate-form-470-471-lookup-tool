"""Regression checks for the data-only APK verifier interface."""
import contextlib
import io
from pathlib import Path
import runpy
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

VERIFIER = Path(__file__).with_name("verify_android_apk.py")
BADGING = """package: name='com.yingyuwang.eratelookup.android' versionCode='113' versionName='1.0.4'
sdkVersion:'26'
launchable-activity: name='com.yingyuwang.eratelookup.android.MainActivity'
uses-permission: name='android.permission.INTERNET'
application-icon-160:'res/drawable/app_icon.png'
"""


class VerifierTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        # Shell metacharacters must remain ordinary file-name data.
        self.apk = root / "preview ; $(touch injected)-Signed.apk"
        self.badging = root / "badging ; $(touch injected).txt"
        self.badging.write_text(BADGING, encoding="utf-8")
        with zipfile.ZipFile(self.apk, "w") as apk:
            for name in ["classes.dex", "assets/LICENSE.txt", "assets/THIRD_PARTY_NOTICES.md"]:
                apk.writestr(name, "fixture")
            for abi, machine in [("arm64-v8a", 183), ("x86_64", 62)]:
                header = bytearray(20)
                header[:6] = b"\x7fELF\x02\x01"
                struct.pack_into("<H", header, 18, machine)
                apk.writestr(f"lib/{abi}/libe_sqlite3.so", header)

    def run_verifier(self, *extra):
        argv = [str(VERIFIER), str(self.apk), "--badging-file", str(self.badging), *extra]
        with patch.object(sys, "argv", argv), contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return runpy.run_path(str(VERIFIER), run_name="__main__")

    def test_metacharacter_paths_are_read_as_data(self):
        self.run_verifier()

    def test_broad_storage_permission_is_rejected(self):
        self.badging.write_text(BADGING + "uses-permission: name='android.permission.MANAGE_EXTERNAL_STORAGE'\n", encoding="utf-8")
        with self.assertRaises(AssertionError):
            self.run_verifier()

    def test_arbitrary_aapt_executable_is_rejected(self):
        with self.assertRaises(SystemExit) as result:
            self.run_verifier("--aapt", "/tmp/attacker-controlled-executable")
        self.assertEqual(result.exception.code, 2)

    def test_arbitrary_apksigner_executable_is_rejected(self):
        with self.assertRaises(SystemExit) as result:
            self.run_verifier("--apksigner", "/tmp/attacker-controlled-executable")
        self.assertEqual(result.exception.code, 2)


if __name__ == "__main__":
    unittest.main()
