#!/usr/bin/env python3
"""M3 *native-host* signature verification for unpacked final distributions.

Read-only: never signs, downloads, uploads or alters a bundle. A successful
run only verifies this path on this OS at this time; it cannot stand for
clean-account installation, OS prompts, license approval or M3 acceptance.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def command(argv, timeout=120):
    result = subprocess.run(argv, text=True, capture_output=True,
                            timeout=timeout, check=False)
    require(result.returncode == 0,
            "Native verification failed (" + argv[0] + "): " +
            (result.stderr or result.stdout).strip()[:650])
    return result.stdout + "\n" + result.stderr


def verify_windows(executable):
    require(sys.platform == "win32", "Authenticode must be checked on native Windows")
    require(executable.is_file() and not executable.is_symlink() and
            executable.suffix.lower() == ".exe", "Final Windows EXE not found")
    # Quote as a literal PowerShell single-quoted string; never interpolate
    # user input into a command fragment where metacharacters can be executed.
    path_literal = str(executable.resolve()).replace("'", "''")
    script = (
        "$ErrorActionPreference='Stop';"
        "$s=Get-AuthenticodeSignature -LiteralPath '" + path_literal + "';"
        "if($s.Status -ne 'Valid' -or $null -eq $s.SignerCertificate)"
        "{throw ('Unsigned/untrusted EXE: ' + $s.Status)};"
        "[PSCustomObject]@{status=$s.Status.ToString();"
        "subject=$s.SignerCertificate.Subject;"
        "thumbprint=$s.SignerCertificate.Thumbprint;"
        "timestampCertificatePresent=($null -ne $s.TimeStamperCertificate)}"
        "| ConvertTo-Json -Compress"
    )
    output = command(["powershell.exe", "-NoProfile", "-NonInteractive",
                      "-Command", script], timeout=120)
    try:
        result = json.loads(output.strip())
    except (json.JSONDecodeError, UnicodeError) as exc:
        raise ValueError("Native Authenticode result was not valid JSON") from exc
    require(type(result) is dict and result.get("status") == "Valid" and
            isinstance(result.get("thumbprint"), str) and
            re.fullmatch(r"[A-Fa-f0-9]{40}", result["thumbprint"]) is not None and
            isinstance(result.get("subject"), str) and result["subject"].strip(),
            "Missing native Authenticode publisher identity")
    # An untrusted/missing timestamp is not acceptable for long-lived release.
    require(result.get("timestampCertificatePresent") is True,
            "Authenticode signature has no independently observed timestamp certificate")
    return {"platform": "windows", "status": "NATIVE_AUTHENTICODE_VALID_ON_HOST",
            "file": executable.name, "signerSubject": result["subject"],
            "signerThumbprint": result["thumbprint"],
            "timestampCertificatePresent": True,
            "cleanMachineAcceptance": False, "distributionAcceptance": False}


def verify_macos(bundle):
    require(sys.platform == "darwin", "Developer ID and Gatekeeper must be checked on macOS")
    require(bundle.is_dir() and not bundle.is_symlink() and bundle.suffix == ".app",
            "Final macOS .app bundle not found")
    executable = bundle / "Contents" / "MacOS" / "VirtualCharacterRender"
    require(executable.is_file() and not executable.is_symlink(),
            "Final macOS executable missing")
    archs = command(["lipo", "-archs", str(executable)])
    require("arm64" in archs.split(), "Final macOS executable has no arm64 slice")
    command(["codesign", "--verify", "--strict", "--deep",
             "--verbose=2", str(bundle)])
    details = command(["codesign", "--display", "--verbose=4", str(bundle)])
    team = re.search(r"(?m)^TeamIdentifier=([A-Z0-9]{10})$", details)
    require(team is not None, "Developer ID Team ID missing")
    require(re.search(r"(?m)^Authority=Developer ID Application:", details) is not None,
            "App is not signed with Developer ID Application identity")
    require(re.search(r"(?m)^CodeDirectory .+flags=.*runtime", details) is not None or
            re.search(r"(?m)^CodeDirectory .+flags=.+\(runtime\)", details) is not None,
            "Hardened runtime signature option missing")
    command(["xcrun", "stapler", "validate", str(bundle)])
    command(["spctl", "--assess", "--type", "execute", "-vv", str(bundle)])
    return {"platform": "macos", "status": "NATIVE_DEVELOPER_ID_NOTARY_GATEKEEPER_VALID_ON_HOST",
            "app": bundle.name, "teamId": team.group(1),
            "arm64Executable": True, "stapleValidated": True,
            "gatekeeperAccepted": True,
            "cleanMachineAcceptance": False, "distributionAcceptance": False}


def self_test():
    # No synthetic trust result can ever be returned by the real verifier.
    # Both operations must fail on a wrong native host or missing binary.
    import tempfile
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        win = root / "missing.exe"
        app = root / "missing.app"
        for verifier, target in ((verify_windows, win), (verify_macos, app)):
            try:
                verifier(target)
            except (ValueError, FileNotFoundError):
                pass
            else:
                raise AssertionError("M3 trust checker accepted absent unsigned artifact")
    print("M3 native signing verifier negative self-test PASS; real signing not exercised")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--platform", choices=("windows", "macos"))
    parser.add_argument("--path", type=Path, help="Final unpacked EXE or stapled .app")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.platform is None or args.path is None:
        parser.error("--platform and --path are required")
    try:
        result = (verify_windows(args.path) if args.platform == "windows"
                  else verify_macos(args.path))
        data = json.dumps(result, indent=2, ensure_ascii=False) + "\n"
        if args.report is not None:
            with args.report.open("x", encoding="utf-8") as output:
                output.write(data)
        print(data, end="")
        return 0
    except (ValueError, OSError, subprocess.SubprocessError,
            json.JSONDecodeError) as exc:
        print("M3 native signature verification FAILED/INCOMPLETE: " +
              str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
