#!/usr/bin/env python3
"""Run an actual Windows/macOS Unity Player, require its startup/shutdown evidence."""
import argparse
import datetime as dt
import hashlib
import json
import os
import plistlib
import platform
from pathlib import Path
import subprocess
import sys
import time
import zipfile


QUIT_MARKER = "VCR exit: OnApplicationQuit finished"
PLAYER_TIMEOUT_SECONDS = 100


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def sidecar(archive, suffix):
    # Native smoke artifacts use "<platform>.sha256"/"<platform>.source-sha"
    # (suffix replaces .zip), while release assets use the standard
    # "<asset>.zip.sha256"/"<asset>.zip.source-sha" convention. Accept both.
    candidates = (
        archive.with_suffix(suffix),
        archive.with_name(archive.name + suffix),
    )
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise RuntimeError(
        "Missing Player archive sidecar: " +
        " or ".join(str(candidate) for candidate in candidates))


def execute(args):
    archive = Path(args.archive).resolve()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    result_path = output / "player-smoke.json"
    logfile = output / "player.log"
    runnerlog = output / "runner.log"
    summary_path = output / "runner-evidence.json"
    summary = {
        "suite": "native-player-smoke-runner",
        "platform": args.platform,
        "runnerPlatform": platform.platform(),
        "sourceSha": args.source,
        "unityVersion": "6000.3.25f1",
        "timestampUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "passed": False,
        "error": "",
    }

    try:
        if not archive.is_file():
            raise RuntimeError("Missing Player archive")
        digest_file = sidecar(archive, ".sha256")
        source_file = sidecar(archive, ".source-sha")
        if source_file.read_text(encoding="utf-8").strip() != args.source:
            raise RuntimeError("Artifact source SHA does not match the tested checkout")

        recorded = digest_file.read_text(encoding="utf-8").split()[0].lower()
        actual = sha256(archive)
        summary["archiveSha256"] = actual
        if recorded != actual:
            raise RuntimeError("Player artifact SHA-256 mismatch")

        # Preserve only logs and hashes as smoke artifacts, not the Player.
        unpacked = output.parent / ("unpacked-" + args.platform)
        unpacked.mkdir(exist_ok=True)
        if args.platform == "macos":
            subprocess.run(
                ["unzip", "-q", str(archive), "-d", str(unpacked)],
                check=True,
                timeout=90,
            )
            bundle = unpacked / "VirtualCharacterRender.app"
            info_path = bundle / "Contents" / "Info.plist"
            if not info_path.is_file():
                raise RuntimeError("macOS Player Info.plist missing")
            with info_path.open("rb") as info:
                executable_name = plistlib.load(info).get("CFBundleExecutable")
            if not isinstance(executable_name, str) or not executable_name:
                raise RuntimeError("macOS Player CFBundleExecutable missing")
            executable = bundle / "Contents" / "MacOS" / executable_name
            if not executable.is_file():
                raise RuntimeError("macOS .app executable missing: " + str(executable))
            executable.chmod(executable.stat().st_mode | 0o111)
            summary["bundleExecutable"] = executable_name
        else:
            with zipfile.ZipFile(archive) as package:
                package.extractall(unpacked)
            executable = unpacked / "VirtualCharacterRender.exe"
            if not executable.is_file():
                raise RuntimeError("Windows .exe Player executable missing")

        summary["playerExecutableSha256"] = sha256(executable)
        command = [
            str(executable),
            "-batchmode",
            "-nographics",
            "-screen-fullscreen", "0",
            "-logFile", str(logfile),
            "--vcr-smoke-report=" + str(result_path),
        ]
        print("Running real " + args.platform + " Player on " + summary["runnerPlatform"], flush=True)
        with runnerlog.open("w", encoding="utf-8") as output_log:
            started_at = time.monotonic()
            process = subprocess.Popen(
                command,
                cwd=unpacked,
                stdout=output_log,
                stderr=subprocess.STDOUT,
            )
            quit_marker_ms = None
            process_exit_ms = None
            try:
                while True:
                    now = time.monotonic()
                    if quit_marker_ms is None and logfile.is_file():
                        try:
                            contents = logfile.read_text(
                                encoding="utf-8", errors="replace")
                        except OSError:
                            contents = ""
                        if QUIT_MARKER in contents:
                            quit_marker_ms = round(
                                (time.monotonic() - started_at) * 1000, 1)
                    return_code = process.poll()
                    if return_code is not None:
                        process_exit_ms = round(
                            (time.monotonic() - started_at) * 1000, 1)
                        summary["exitCode"] = return_code
                        break
                    if now - started_at > PLAYER_TIMEOUT_SECONDS:
                        summary["exitCode"] = None
                        raise RuntimeError(
                            "Player did not exit within " +
                            str(PLAYER_TIMEOUT_SECONDS) + " seconds")
                    time.sleep(0.025)
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()
                if process_exit_ms is None:
                    process_exit_ms = round(
                        (time.monotonic() - started_at) * 1000, 1)
                # End-to-end subprocess lifetime is a useful CI baseline,
                # but includes startup and does not prove GUI quit latency.
                summary["playerProcessLifetimeMs"] = process_exit_ms
                summary["quitMarkerMs"] = quit_marker_ms
                summary["quitTailMs"] = (
                    round(process_exit_ms - quit_marker_ms, 1)
                    if quit_marker_ms is not None else None
                )
                print(
                    "Player process lifetime (startup + shutdown): " +
                    str(summary["playerProcessLifetimeMs"]) + " ms",
                    flush=True)
                if summary["quitTailMs"] is not None:
                    print(
                        "Quit marker to process exit: " +
                        str(summary["quitTailMs"]) + " ms",
                        flush=True)

        if not result_path.is_file():
            raise RuntimeError("Player did not write its runtime smoke evidence")
        evidence = json.loads(result_path.read_text(encoding="utf-8"))
        summary["playerReport"] = evidence
        for field in ("started", "sceneReady", "noCharacter", "stopped", "passed"):
            if evidence.get(field) is not True:
                raise RuntimeError("Player did not confirm " + field + ": " + str(evidence.get("error")))
        if evidence.get("suite") != "player-startup-shutdown":
            raise RuntimeError("Unexpected Player smoke suite")
        if evidence.get("unityVersion") != "6000.3.25f1":
            raise RuntimeError("Player Unity version mismatch")
        if args.platform == "windows" and evidence.get("platform") != "WindowsPlayer":
            raise RuntimeError("Expected a native Windows Player")
        if args.platform == "macos" and evidence.get("platform") != "OSXPlayer":
            raise RuntimeError("Expected a native macOS Player")
        if summary["exitCode"] != 0:
            raise RuntimeError("Player failed with exit code " + str(summary["exitCode"]))
        if args.require_quit_marker and summary.get("quitMarkerMs") is None:
            raise RuntimeError(
                "Player did not emit its quit marker: " + QUIT_MARKER)

        summary["passed"] = True
        print("Native Player startup/shutdown: PASS", flush=True)
    except Exception as exception:
        summary["error"] = str(exception)
        print("Native Player startup/shutdown: FAIL: " + str(exception), file=sys.stderr)
        if logfile.is_file():
            tail = logfile.read_text(encoding="utf-8", errors="replace").splitlines()[-60:]
            print("Player.log last 60 lines:\n" + "\n".join(tail), file=sys.stderr)
    finally:
        summary_path.write_text(
            json.dumps(summary, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        print("Native evidence: " + str(summary_path), flush=True)
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    cli = argparse.ArgumentParser()
    cli.add_argument("--platform", choices=("windows", "macos"), required=True)
    cli.add_argument("--archive", required=True)
    cli.add_argument("--source", required=True)
    cli.add_argument("--output", required=True)
    cli.add_argument(
        "--require-quit-marker",
        action="store_true",
        help="fail unless Player.log contains the managed quit marker before process exit",
    )
    sys.exit(execute(cli.parse_args()))
