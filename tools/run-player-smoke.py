#!/usr/bin/env python3
"""Run an actual Windows/macOS Unity Player, require its startup/shutdown evidence."""
import argparse
import datetime as dt
import hashlib
import json
import os
import platform
from pathlib import Path
import subprocess
import sys
import zipfile


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def execute(args):
    archive = Path(args.archive).resolve()
    digest_file = archive.with_suffix(".sha256")
    source_file = archive.with_suffix(".source-sha")
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
        if not archive.is_file() or not digest_file.is_file() or not source_file.is_file():
            raise RuntimeError("Missing Player archive, SHA-256 or source provenance")
        if source_file.read_text(encoding="utf-8").strip() != args.source:
            raise RuntimeError("Artifact source SHA does not match the tested checkout")

        recorded = digest_file.read_text(encoding="utf-8").split()[0].lower()
        actual = sha256(archive)
        summary["archiveSha256"] = actual
        if recorded != actual:
            raise RuntimeError("Player artifact SHA-256 mismatch")

        unpacked = output / "player"
        unpacked.mkdir(exist_ok=True)
        if args.platform == "macos":
            subprocess.run(
                ["unzip", "-q", str(archive), "-d", str(unpacked)],
                check=True,
                timeout=90,
            )
            executable = (
                unpacked / "VirtualCharacterRender.app" /
                "Contents" / "MacOS" / "VirtualCharacterRender"
            )
            if not executable.is_file():
                raise RuntimeError("macOS .app Player executable missing")
            executable.chmod(executable.stat().st_mode | 0o111)
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
            try:
                completed = subprocess.run(
                    command,
                    cwd=unpacked,
                    stdout=output_log,
                    stderr=subprocess.STDOUT,
                    timeout=100,
                    check=False,
                )
                summary["exitCode"] = completed.returncode
            except subprocess.TimeoutExpired as exception:
                summary["exitCode"] = None
                raise RuntimeError("Player did not exit within 100 seconds") from exception

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
    sys.exit(execute(cli.parse_args()))
