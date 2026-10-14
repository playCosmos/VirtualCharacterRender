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


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def run_player_with_exit_trace(command, cwd, logfile, output_log, timeout=100):
    """Time the real subprocess exit from the Unity wantsToQuit marker.

    This does not hide any window, alter shutdown sequencing, or force
    termination except on the existing CI timeout. The marker comes from
    the normal Unity Player.log; the read is best-effort until exit.
    """
    started = time.monotonic()
    marker_at = None
    offset = 0
    carry = b""
    marker = b"VCR exit: quit requested (t=0 ms)"
    def drain_marker():
        nonlocal marker_at, offset, carry
        if marker_at is not None or not logfile.exists():
            return
        try:
            with logfile.open("rb") as source:
                source.seek(offset)
                chunk = source.read()
                offset = source.tell()
            if chunk:
                combined = carry + chunk
                if marker in combined:
                    marker_at = time.monotonic()
                carry = combined[-(len(marker) - 1):]
        except OSError:
            # Unity log is best-effort while it is held by another process.
            pass

    with output_log.open("w", encoding="utf-8") as output_stream:
        process = subprocess.Popen(
            command, cwd=cwd, stdout=output_stream,
            stderr=subprocess.STDOUT)
        try:
            while process.poll() is None:
                drain_marker()
                if time.monotonic() - started > timeout:
                    raise subprocess.TimeoutExpired(command, timeout)
                time.sleep(0.05)
            return_code = process.returncode
            # Do not lose buffered quit markers from fast processes.
            drain_marker()
        except BaseException as exception:
            drain_marker()
            elapsed = round((time.monotonic() - started) * 1000, 1)
            # These durations describe the process BEFORE timeout/kill. They
            # are not a successful quit-to-process-exit measurement.
            exception.player_process_lifetime_ms = elapsed
            exception.quit_to_timeout_ms = (
                round((time.monotonic() - marker_at) * 1000, 1)
                if marker_at is not None else None)
            exception.quit_marker_observed = marker_at is not None
            if process.poll() is None:
                process.kill()  # CI-only bound, never the interactive observer.
            process.wait()
            raise
    finished = time.monotonic()
    return (
        return_code,
        round((finished - started) * 1000, 1),
        round((finished - marker_at) * 1000, 1)
        if marker_at is not None else None,
    )


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
        try:
            code, lifetime_ms, exit_ms = run_player_with_exit_trace(
                command, unpacked, logfile, runnerlog, timeout=100)
            summary["exitCode"] = code
            summary["playerProcessLifetimeMs"] = lifetime_ms
            summary["quitToProcessExitMs"] = exit_ms
            print("Player process lifetime (startup + shutdown): " +
                  str(lifetime_ms) + " ms", flush=True)
            print("Quit marker to process exit (best-effort): " +
                  str(exit_ms) + " ms", flush=True)
        except subprocess.TimeoutExpired as exception:
            summary["exitCode"] = None
            summary["timedOut"] = True
            summary["playerProcessLifetimeMs"] = getattr(
                exception, "player_process_lifetime_ms", 100000)
            summary["quitToTimeoutMs"] = getattr(
                exception, "quit_to_timeout_ms", None)
            summary["quitMarkerObserved"] = getattr(
                exception, "quit_marker_observed", False)
            summary["quitToProcessExitMs"] = None
            raise RuntimeError("Player did not exit within 100 seconds; " +
                               "timing evidence retained") from exception

        if not result_path.is_file():
            raise RuntimeError("Player did not write its runtime smoke evidence")
        evidence = json.loads(result_path.read_text(encoding="utf-8"))
        summary["playerReport"] = evidence
        for field in ("started", "sceneReady", "noCharacter", "stopped", "passed"):
            if evidence.get(field) is not True:
                raise RuntimeError("Player did not confirm " + field + ": " + str(evidence.get("error")))
        # Prevent regressions to the former smoke test which called
        # bootstrap.Shutdown() before Application.Quit().
        managed_ms = evidence.get("quitManagedMilliseconds")
        if (not isinstance(managed_ms, int) or isinstance(managed_ms, bool)
                or managed_ms < 0):
            raise RuntimeError("Normal Unity quit cleanup evidence missing")
        summary["quitManagedMilliseconds"] = managed_ms
        if evidence.get("suite") != "player-startup-shutdown":
            raise RuntimeError("Unexpected Player smoke suite")
        if evidence.get("unityVersion") != "6000.3.25f1":
            raise RuntimeError("Player Unity version mismatch")
        if args.platform.startswith("windows") and evidence.get("platform") != "WindowsPlayer":
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
    cli.add_argument("--platform", choices=("windows", "windows-nodebug", "macos"), required=True)
    cli.add_argument("--archive", required=True)
    cli.add_argument("--source", required=True)
    cli.add_argument("--output", required=True)
    sys.exit(execute(cli.parse_args()))
