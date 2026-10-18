#!/usr/bin/env python3
"""M3 *native-host* signature verification for unpacked final distributions.

Read-only: never signs, downloads, uploads or alters a bundle. A successful
run only verifies this path on this OS at this time; it cannot stand for
clean-account installation, OS prompts, license approval or M3 acceptance.
"""
import argparse
import hashlib
import json
from pathlib import Path
import posixpath
import re
import stat
import subprocess
import sys
import zipfile


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


def sha256_stream(stream):
    digest = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(block)
    return digest.hexdigest()


def validate_archive_binding(final_path, archive_path, expected_sha, platform):
    """Prove that the signed host-checked files are exactly those in the ZIP.

    This compares every ZIP member to the on-disk extracted distribution,
    including macOS framework symlink targets. It is read-only and does not
    certify any source manifest or signature by itself.
    """
    require(re.fullmatch(r"[0-9a-f]{64}", expected_sha or "") is not None,
            "Exact final archive SHA256 required for native binding")
    require(archive_path.is_file() and not archive_path.is_symlink(),
            "Final signed ZIP missing/unsafe")
    with archive_path.open("rb") as source:
        actual = sha256_stream(source)
    require(actual == expected_sha, "Final signed ZIP does not match expected SHA256")
    root = final_path.parent
    seen = set()
    checked = 0
    total_uncompressed_bytes = 0
    with zipfile.ZipFile(archive_path) as package:
        members = package.infolist()
        require(0 < len(members) <= 100000,
                "Unreasonable final signed ZIP member count")
        for item in members:
            total_uncompressed_bytes += item.file_size
            require(total_uncompressed_bytes <= 16 * 1024**3,
                    "Final signed ZIP uncompressed content too large")
            name = item.filename
            while name.startswith("./"):
                name = name[2:]
            require(name and not name.startswith("/") and
                    "\\" not in name and ":" not in name and
                    ".." not in name.split("/") and
                    posixpath.normpath(name) not in ("..", "."),
                    "Unsafe ZIP distribution member")
            normalized = posixpath.normpath(name)
            require(not normalized.startswith("../") and
                    normalized not in seen,
                    "ZIP distribution traversal or duplicate")
            seen.add(normalized)
            if item.is_dir():
                continue
            target = root.joinpath(*normalized.split("/"))
            kind = stat.S_IFMT(item.external_attr >> 16)
            require(kind in (0, stat.S_IFREG, stat.S_IFLNK),
                    "Unsupported signed archive special file")
            if kind == stat.S_IFLNK:
                require(target.is_symlink() and item.file_size <= 4096,
                        "Packaged symlink absent from signed app")
                link = package.read(item).decode("utf-8")
                require(target.readlink().as_posix() == link,
                        "Signed bundle symlink disagrees with ZIP")
            else:
                require(target.is_file() and not target.is_symlink() and
                        target.stat().st_size == item.file_size,
                        "Packaged payload not equal to signed local distribution")
                with package.open(item) as stream:
                    zip_hash = sha256_stream(stream)
                with target.open("rb") as stream:
                    disk_hash = sha256_stream(stream)
                require(zip_hash == disk_hash,
                        "ZIP payload does not match locally signed file: " + normalized)
            checked += 1
    require(checked > 0, "No signed archive members matched")
    if platform == "windows":
        require("VirtualCharacterRender.exe" in seen,
                "Bound ZIP lacks the signed Windows EXE")
    else:
        require(final_path.name + "/Contents/Info.plist" in seen and
                final_path.name + "/Contents/MacOS/VirtualCharacterRender" in seen,
                "Bound ZIP lacks required signed macOS app entries")
    return {"finalZipSha256": actual,
            "matchingArchiveMembers": checked,
            "archiveBindingVerified": True}


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
        signed = root / "VirtualCharacterRender.exe"
        signed.write_bytes(b"MZ synthetic unsigned")
        zpath = root / "final.zip"
        with zipfile.ZipFile(zpath, "w") as package:
            package.writestr(signed.name, signed.read_bytes())
        with zpath.open("rb") as stream:
            digest = sha256_stream(stream)
        bound = validate_archive_binding(signed, zpath, digest, "windows")
        assert bound["archiveBindingVerified"] is True
        signed.write_bytes(b"MZ replaced payload")
        try:
            validate_archive_binding(signed, zpath, digest, "windows")
        except ValueError:
            pass
        else:
            raise AssertionError("Substituted unsigned binary matched final ZIP")
        signed.write_bytes(b"MZ synthetic unsigned")
        try:
            validate_archive_binding(signed, zpath, "0" * 64, "windows")
        except ValueError:
            pass
        else:
            raise AssertionError("Final signed ZIP SHA mismatch accepted")
    print("M3 native signing verifier negative self-test PASS; real signing not exercised")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--platform", choices=("windows", "macos"))
    parser.add_argument("--path", type=Path, help="Final unpacked EXE or stapled .app")
    parser.add_argument("--report", type=Path)
    parser.add_argument("--archive", type=Path, help="Final signed ZIP distributed to users")
    parser.add_argument("--archive-sha256", help="Expected independently rehashed final ZIP SHA256")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.platform is None or args.path is None:
        parser.error("--platform and --path are required")
    try:
        require((args.archive is None) == (args.archive_sha256 is None),
                "Provide both final --archive and --archive-sha256 together")
        # Bind the actual signed/notarized local payload to the distribution
        # bytes *before* trusting the host OS signature check. This prevents
        # verifying a signed staging EXE/app but shipping different ZIP files.
        binding = (validate_archive_binding(args.path, args.archive,
                                            args.archive_sha256, args.platform)
                   if args.archive is not None else {"archiveBindingVerified": False})
        result = (verify_windows(args.path) if args.platform == "windows"
                  else verify_macos(args.path))
        result.update(binding)
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
