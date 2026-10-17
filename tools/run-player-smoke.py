#!/usr/bin/env python3
"""Run an actual Windows/macOS Unity Player, require its startup/shutdown evidence."""
import argparse
import datetime as dt
import hashlib
import json
import os
import posixpath
import plistlib
import platform
import shutil
from pathlib import Path
import stat
import subprocess
import sys
import time
import zipfile


# Generous headless regression budget: the no-debug Windows baseline may
# spend ~2 s in Unity's native teardown even with trivial managed cleanup.
# A 10-second quit tail indicates a severe regression, not ordinary jitter.
MAX_HEADLESS_QUIT_TO_EXIT_MS = 10_000
NATIVE_CONFIG_PROBE_FOV = 42.5
MAX_NATIVE_CONFIG_BYTES = 4 * 1024 * 1024


def require_headless_quit_budget(exit_ms):
    if exit_ms is not None and exit_ms > MAX_HEADLESS_QUIT_TO_EXIT_MS:
        raise RuntimeError(
            "Headless quit-to-PID-exit latency regression: " +
            str(exit_ms) + " ms exceeds " +
            str(MAX_HEADLESS_QUIT_TO_EXIT_MS) + " ms")


# CI artifacts are produced from pull-request source and extracted on native
# operating systems. Digest matching does not make archive member paths safe.
MAX_ARCHIVE_MEMBERS = 100_000
MAX_ARCHIVE_EXPANDED_BYTES = 8 * 1024 * 1024 * 1024


def validate_player_archive(archive):
    """Reject archive traversal, escaping links, duplicates and ZIP bombs.

    Native macOS Players legitimately contain in-bundle symlinks; allow only
    relative links whose normalized targets stay under the extraction root.
    The archive is checked *before* invoking macOS unzip or Windows extractall.
    """
    seen = set()
    total = 0
    with zipfile.ZipFile(archive) as package:
        members = package.infolist()
        if not members or len(members) > MAX_ARCHIVE_MEMBERS:
            raise RuntimeError("Invalid Player archive entry count")
        for entry in members:
            raw = entry.filename
            if (not raw or "\\" in raw or "\x00" in raw or
                    raw.startswith("/") or raw.startswith("\\\\") or
                    any(":" in part or part == ".."
                        for part in raw.split("/"))):
                raise RuntimeError("Unsafe Player archive path: " + repr(raw))
            name = posixpath.normpath(raw)
            if name == ".":
                if not entry.is_dir():
                    raise RuntimeError("Invalid Player archive root member")
                continue
            if name in seen:
                raise RuntimeError("Duplicate Player archive path: " + name)
            seen.add(name)
            if name == ".." or name.startswith("../"):
                raise RuntimeError("Escaping Player archive path: " + name)
            total += entry.file_size
            if (entry.file_size < 0 or entry.file_size > MAX_ARCHIVE_EXPANDED_BYTES or
                    total > MAX_ARCHIVE_EXPANDED_BYTES):
                raise RuntimeError("Oversized Player archive expansion")
            mode = (entry.external_attr >> 16) & 0o170000
            if mode == stat.S_IFLNK:
                if entry.file_size > 4096:
                    raise RuntimeError("Oversized Player archive link target")
                target = package.read(entry).decode("utf-8", errors="strict")
                if (not target or target.startswith("/") or
                        "\\" in target or "\x00" in target or
                        any(":" in part for part in target.split("/"))):
                    raise RuntimeError("Unsafe Player archive symlink: " + name)
                destination = posixpath.normpath(
                    posixpath.join(posixpath.dirname(name), target))
                if destination == ".." or destination.startswith("../"):
                    raise RuntimeError("Escaping Player archive symlink: " + name)


def require_build_variant(platform_name, development_build):
    """Verify that 'nodebug' artifacts are actually built without Dev flags."""
    if type(development_build) is not bool:
        raise RuntimeError("Native Player omitted development-build evidence")
    expected = not platform_name.endswith("-nodebug")
    if development_build is not expected:
        raise RuntimeError(
            "Player development mode mismatch: expected " +
            str(expected) + " for " + platform_name +
            ", reported " + str(development_build))


def require_commit_sha(value, label):
    if (not isinstance(value, str) or len(value) != 40 or
            any(c not in "0123456789abcdef" for c in value)):
        raise RuntimeError(label + " is not an exact 40-hex Git commit")


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def smoke_command(executable, logfile, report_path, config_path):
    return [
        str(executable), "-batchmode", "-nographics",
        "-screen-fullscreen", "0", "-logFile", str(logfile),
        "--vcr-smoke-report=" + str(report_path),
        "--vcr-config=" + str(config_path),
    ]


def read_smoke_configuration(path):
    if not path.is_file() or path.stat().st_size > MAX_NATIVE_CONFIG_BYTES:
        raise RuntimeError("Native Player did not save a bounded runtime configuration")
    payload = json.loads(path.read_text(encoding="utf-8"))
    if type(payload) is not dict or type(payload.get("Version")) is not int or payload["Version"] != 1:
        raise RuntimeError("Native Player configuration version is missing or unexpected")
    scene = payload.get("Scene")
    camera = scene.get("Camera") if type(scene) is dict else None
    fov = camera.get("FieldOfView") if type(camera) is dict else None
    if type(fov) not in (int, float) or not 1 <= fov <= 179:
        raise RuntimeError("Native Player configuration camera FOV is invalid")
    return payload


def require_smoke_relaunch_evidence(report, platform_name, expected_fov):
    if report.get("suite") != "player-startup-shutdown":
        raise RuntimeError("Relaunch smoke suite mismatch")
    if report.get("unityVersion") != "6000.3.25f1":
        raise RuntimeError("Relaunch Unity version mismatch")
    expected_platform = ("WindowsPlayer" if platform_name.startswith("windows")
                         else "OSXPlayer")
    if report.get("platform") != expected_platform:
        raise RuntimeError("Relaunch native platform mismatch")
    require_build_variant(platform_name, report.get("developmentBuild"))
    for field in ("started", "sceneReady", "noCharacter", "stopped", "passed"):
        if report.get(field) is not True:
            raise RuntimeError("Relaunch failed runtime evidence: " + field)
    observed = report.get("observedCameraFieldOfView")
    if type(observed) not in (float, int) or abs(observed - expected_fov) > 0.05:
        raise RuntimeError(
            "Native Player did not reapply saved configuration; camera FOV " +
            repr(observed) + " vs expected " + repr(expected_fov))
    for field in ("quitCallToManagedCallbackMs", "quitMarkerToManagedCallbackMs"):
        value = report.get(field)
        if type(value) is not int or value < 0:
            raise RuntimeError("Relaunch quit timing evidence missing: " + field)


def run_player_with_exit_trace(command, cwd, logfile, output_log, timeout=100):
    """Time the real subprocess exit from the Unity wantsToQuit marker.

    This does not hide any window, alter shutdown sequencing, or force
    termination except on the existing CI timeout. The marker comes from
    the normal Unity Player.log; the read is best-effort until exit.
    """
    # Never attribute a marker from a previous failed/retried CI run to
    # the newly launched subprocess. Windows sharing violations must FAIL.
    logfile.unlink(missing_ok=True)
    started = time.monotonic()
    marker_at = None
    marker_observed_after_exit = False
    offset = 0
    carry = b""
    marker = b"VCR exit: quit requested (t=0 ms)"
    def drain_marker(after_exit=False):
        nonlocal marker_at, marker_observed_after_exit, offset, carry
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
                    marker_observed_after_exit = after_exit
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
            drain_marker(after_exit=True)
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
        if marker_at is not None and not marker_observed_after_exit else None,
        marker_observed_after_exit,
    )


def execute(args):
    archive = Path(args.archive).resolve()
    digest_file = archive.with_suffix(".sha256")
    source_file = archive.with_suffix(".source-sha")
    head_file = archive.with_suffix(".pr-head-sha")
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
        "testedSourceSha": args.source,
        "pullRequestHeadSha": args.pr_head,
        "testedSourceIsPullRequestMerge": args.source != args.pr_head,
        "unityVersion": "6000.3.25f1",
        "timestampUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "passed": False,
        "error": "",
    }

    try:
        require_commit_sha(args.source, "Tested source SHA")
        require_commit_sha(args.pr_head, "Pull-request head SHA")
        if (not archive.is_file() or not digest_file.is_file() or
                not source_file.is_file() or not head_file.is_file()):
            raise RuntimeError("Missing Player archive, SHA-256 or source/head provenance")
        if source_file.read_text(encoding="utf-8").strip() != args.source:
            raise RuntimeError("Artifact tested-source SHA does not match the runner")
        if head_file.read_text(encoding="utf-8").strip() != args.pr_head:
            raise RuntimeError("Artifact PR-head SHA does not match the runner")

        recorded = digest_file.read_text(encoding="utf-8").split()[0].lower()
        actual = sha256(archive)
        summary["archiveSha256"] = actual
        if recorded != actual:
            raise RuntimeError("Player artifact SHA-256 mismatch")
        validate_player_archive(archive)
        summary["archiveSafetyValidated"] = True

        # Preserve only logs and hashes as smoke artifacts, not the Player.
        unpacked = output.parent / ("unpacked-" + args.platform)
        if unpacked.is_symlink():
            raise RuntimeError("Refusing symlink extraction directory")
        if unpacked.exists():
            shutil.rmtree(unpacked)
        unpacked.mkdir(exist_ok=False)
        if args.platform.startswith("macos"):
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
            if (not isinstance(executable_name, str) or not executable_name or
                    executable_name in (".", "..") or
                    any(ch in executable_name for ch in ("/", "\\", ":", "\x00"))):
                raise RuntimeError("Unsafe macOS Player CFBundleExecutable")
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
        # Isolation is essential: the smoke must not read a developer/runner's
        # real persistentDataPath or create user-facing Unity preferences.
        config_dir = output / "isolated-runtime-config"
        if config_dir.is_symlink():
            raise RuntimeError("Refusing symlink smoke configuration directory")
        if config_dir.exists():
            shutil.rmtree(config_dir)
        config_dir.mkdir()
        config_path = config_dir / "runtime-config.json"
        summary["configurationIsolated"] = True
        # Do not accept smoke evidence left behind by a prior job attempt.
        result_path.unlink(missing_ok=True)
        command = smoke_command(executable, logfile, result_path, config_path)
        print("Running real " + args.platform + " Player on " + summary["runnerPlatform"], flush=True)
        try:
            code, lifetime_ms, exit_ms, marker_after_exit = run_player_with_exit_trace(
                command, unpacked, logfile, runnerlog, timeout=100)
            summary["exitCode"] = code
            summary["playerProcessLifetimeMs"] = lifetime_ms
            summary["quitToProcessExitMs"] = exit_ms
            summary["quitMarkerObservedAfterExit"] = marker_after_exit
            if marker_after_exit:
                summary["quitToProcessExitLowerBoundMs"] = 0
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
        # Require two distinct clocks: Application.Quit() can block before
        # wantsToQuit dispatch, while the bootstrap timer starts at the
        # actual Unity exit marker. Never call the first one cleanup time.
        call_to_callback = evidence.get("quitCallToManagedCallbackMs")
        marker_to_callback = evidence.get("quitMarkerToManagedCallbackMs")
        if any(not isinstance(value, int) or isinstance(value, bool) or
               value < 0 for value in (call_to_callback, marker_to_callback)):
            raise RuntimeError("Normal Unity quit timing evidence missing")
        if marker_to_callback > call_to_callback:
            raise RuntimeError("Unity quit marker elapsed beyond initial quit call")
        summary["quitCallToManagedCallbackMs"] = call_to_callback
        summary["quitMarkerToManagedCallbackMs"] = marker_to_callback
        summary["quitDispatchBeforeMarkerMs"] = call_to_callback - marker_to_callback
        # The OS process timer observes a Player.log marker during polling.
        # This subtraction is an approximation (up to one poll interval),
        # NOT a precise native stack/teardown measurement.
        exit_ms = summary.get("quitToProcessExitMs")
        summary["postManagedToProcessExitEstimateMs"] = (
            round(max(0, exit_ms - marker_to_callback), 1)
            if exit_ms is not None else None
        )
        summary["maxHeadlessQuitToExitMs"] = MAX_HEADLESS_QUIT_TO_EXIT_MS
        require_headless_quit_budget(exit_ms)
        if (summary["postManagedToProcessExitEstimateMs"] is not None and
                summary["postManagedToProcessExitEstimateMs"] >= 2_000):
            print("Native teardown estimate exceeds 2000 ms: " +
                  str(summary["postManagedToProcessExitEstimateMs"]) +
                  " ms (poll-resolution estimate; not a managed cleanup delay)",
                  flush=True)
        if evidence.get("suite") != "player-startup-shutdown":
            raise RuntimeError("Unexpected Player smoke suite")
        if evidence.get("unityVersion") != "6000.3.25f1":
            raise RuntimeError("Player Unity version mismatch")
        require_build_variant(args.platform, evidence.get("developmentBuild"))
        summary["developmentBuild"] = evidence["developmentBuild"]
        if args.platform.startswith("windows") and evidence.get("platform") != "WindowsPlayer":
            raise RuntimeError("Expected a native Windows Player")
        if args.platform.startswith("macos") and evidence.get("platform") != "OSXPlayer":
            raise RuntimeError("Expected a native macOS Player")
        if summary["exitCode"] != 0:
            raise RuntimeError("Player failed with exit code " + str(summary["exitCode"]))

        # Prove real normal quit persisted bounded, schema-valid settings.
        # A second process must load a deliberately non-default camera FOV:
        # checking file existence alone would not prove configuration restore.
        first_config = read_smoke_configuration(config_path)
        backup_path = config_path.with_name(config_path.name + ".bak")
        if backup_path.exists():
            raise RuntimeError("Fresh first-run configuration unexpectedly has a backup")
        first_fov = first_config["Scene"]["Camera"]["FieldOfView"]
        if abs(first_fov - NATIVE_CONFIG_PROBE_FOV) < 0.05:
            raise RuntimeError("Config probe FOV is not distinct from scene default")
        summary["firstRunSavedConfiguration"] = True
        summary["firstRunCameraFieldOfView"] = first_fov
        first_config["Scene"]["Camera"]["FieldOfView"] = NATIVE_CONFIG_PROBE_FOV
        config_path.write_text(json.dumps(first_config, ensure_ascii=False), encoding="utf-8")
        if abs(read_smoke_configuration(config_path)["Scene"]["Camera"]["FieldOfView"] -
               NATIVE_CONFIG_PROBE_FOV) > 0.05:
            raise RuntimeError("Could not write smoke config restore marker")

        relaunch_report_path = output / "relaunch-player-smoke.json"
        relaunch_log = output / "relaunch-player.log"
        relaunch_runnerlog = output / "relaunch-runner.log"
        relaunch_report_path.unlink(missing_ok=True)
        relaunch_command = smoke_command(
            executable, relaunch_log, relaunch_report_path, config_path)
        print("Relaunching native Player with isolated persisted config", flush=True)
        relaunch_code, relaunch_lifetime, relaunch_exit, relaunch_marker_late = (
            run_player_with_exit_trace(
                relaunch_command, unpacked, relaunch_log, relaunch_runnerlog,
                timeout=100))
        summary["relaunch"] = {
            "exitCode": relaunch_code,
            "playerProcessLifetimeMs": relaunch_lifetime,
            "quitToProcessExitMs": relaunch_exit,
            "quitMarkerObservedAfterExit": relaunch_marker_late,
            "expectedCameraFieldOfView": NATIVE_CONFIG_PROBE_FOV,
            "passed": False,
        }
        if relaunch_code != 0 or not relaunch_report_path.is_file():
            raise RuntimeError("Native Player relaunch failed before runtime report")
        relaunch_report = json.loads(relaunch_report_path.read_text(encoding="utf-8"))
        require_smoke_relaunch_evidence(
            relaunch_report, args.platform, NATIVE_CONFIG_PROBE_FOV)
        require_headless_quit_budget(relaunch_exit)
        saved_fov = read_smoke_configuration(config_path)["Scene"]["Camera"]["FieldOfView"]
        if not backup_path.is_file():
            raise RuntimeError("Native Player relaunch did not rotate a saved backup")
        backup_fov = read_smoke_configuration(backup_path)["Scene"]["Camera"]["FieldOfView"]
        if (abs(saved_fov - NATIVE_CONFIG_PROBE_FOV) > 0.05 or
                abs(backup_fov - NATIVE_CONFIG_PROBE_FOV) > 0.05):
            raise RuntimeError("Native Player persisted or backup FOV differs after relaunch")
        summary["relaunch"]["observedCameraFieldOfView"] = (
            relaunch_report["observedCameraFieldOfView"])
        summary["relaunch"]["backupVerified"] = True
        summary["relaunch"]["passed"] = True
        summary["passed"] = True
        print("Native Player first-run + persisted-configuration relaunch: PASS",
              flush=True)
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



def self_test():
    """Prove a fast natural exit and a slow forced CI timeout both keep evidence."""
    from tempfile import TemporaryDirectory
    with TemporaryDirectory() as dirname:
        root = Path(dirname)
        stub = root / "fake-player.py"
        stub.write_text(
            "import pathlib, sys, time\n"
            "log = pathlib.Path(sys.argv[sys.argv.index('-logFile') + 1])\n"
            "log.write_text('VCR exit: quit requested (t=0 ms)\\n')\n"
            "if '--hang' in sys.argv: time.sleep(5)\n"
            "else: log.write_text(log.read_text() + "
            "'VCR exit: OnApplicationQuit finished at 7 ms\\n')\n",
            encoding="utf-8",
        )
        command = [sys.executable, str(stub), "-logFile", str(root / "fast.log")]
        (root / "fast.log").write_text(
            "VCR exit: quit requested (t=0 ms)\\nSTALE RUN\\n", encoding="utf-8")
        code, lifetime, quit_ms, late = run_player_with_exit_trace(
            command, root, root / "fast.log", root / "fast.out", timeout=4)
        assert code == 0 and lifetime >= 0
        assert (late and quit_ms is None) or (not late and quit_ms is not None)
        assert "STALE RUN" not in (root / "fast.log").read_text()

        # Force a post-exit first drain: never turn it into a fabricated
        # near-zero marker-to-PID interval.
        from unittest.mock import patch
        real_sleep = time.sleep
        def wait_past_quick_exit(seconds):
            real_sleep(max(seconds, 0.15))
        with patch.object(time, "sleep", wait_past_quick_exit):
            code, lifetime, quit_ms, late = run_player_with_exit_trace(
                command, root, root / "fast.log", root / "fast-late.out", timeout=4)
        assert code == 0 and late and quit_ms is None

        log = root / "hung.log"
        command = [sys.executable, str(stub), "-logFile", str(log), "--hang"]
        try:
            run_player_with_exit_trace(
                command, root, log, root / "hung.out", timeout=0.75)
            raise AssertionError("Expected CI timeout with preserved evidence")
        except subprocess.TimeoutExpired as exception:
            assert exception.player_process_lifetime_ms >= 700
            assert exception.quit_marker_observed is True
            assert exception.quit_to_timeout_ms is not None
    # Native ZIP extraction must reject escaping members/links *before*
    # invoking Windows ZipFile.extractall or the macOS unzip subprocess.
    with TemporaryDirectory() as zdir:
        folder = Path(zdir)
        zip_path = folder / "player.zip"
        def write_archive(entries):
            with zipfile.ZipFile(zip_path, "w") as archive:
                for name, data, is_symlink in entries:
                    entry = zipfile.ZipInfo(name)
                    if is_symlink:
                        entry.create_system = 3
                        entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                    archive.writestr(entry, data)
        write_archive([
            ("VirtualCharacterRender.app/Contents/MacOS/player", b"player", False),
            ("VirtualCharacterRender.app/Contents/Frameworks/link",
             b"../MacOS/player", True),
        ])
        validate_player_archive(zip_path)
        for bad in (
            [("../outside", b"unsafe", False)],
            [("/absolute/file", b"unsafe", False)],
            [("C:/windows.exe", b"unsafe", False)],
            [("app/Contents/../../outside", b"unsafe", False)],
            [("safe/file", b"a", False), ("safe/file", b"b", False)],
            [("app/Contents/link", b"../../../outside", True)],
            [("app/Contents/link", b"/etc/passwd", True)],
        ):
            write_archive(bad)
            try:
                validate_player_archive(zip_path)
            except RuntimeError:
                pass
            else:
                raise AssertionError("Unsafe Player archive unexpectedly accepted")
    for variant in ("windows", "windows-nodebug", "macos", "macos-nodebug"):
        expected = not variant.endswith("-nodebug")
        require_build_variant(variant, expected)
        for wrong in (not expected, None, "false", 0):
            try:
                require_build_variant(variant, wrong)
            except RuntimeError:
                pass
            else:
                raise AssertionError("Wrong Player build mode accepted")
    # Configuration relaunch must prove an observed runtime value, not
    # merely the presence of a JSON file in the CI extraction directory.
    with TemporaryDirectory() as cdir:
        path = Path(cdir) / "runtime.json"
        valid = {
            "Version": 1,
            "Scene": {"Camera": {"FieldOfView": NATIVE_CONFIG_PROBE_FOV}},
        }
        path.write_text(json.dumps(valid), encoding="utf-8")
        assert read_smoke_configuration(path)["Version"] == 1
        sample = {
            "suite": "player-startup-shutdown",
            "unityVersion": "6000.3.25f1",
            "platform": "WindowsPlayer",
            "developmentBuild": True,
            "observedCameraFieldOfView": NATIVE_CONFIG_PROBE_FOV,
            "quitCallToManagedCallbackMs": 11,
            "quitMarkerToManagedCallbackMs": 8,
            "started": True,
            "sceneReady": True,
            "noCharacter": True,
            "stopped": True,
            "passed": True,
        }
        require_smoke_relaunch_evidence(sample, "windows", NATIVE_CONFIG_PROBE_FOV)
        for changes in (
            {"observedCameraFieldOfView": 35.0},
            {"developmentBuild": False},
            {"passed": False},
            {"platform": "OSXPlayer"},
            {"quitMarkerToManagedCallbackMs": None},
        ):
            invalid = dict(sample, **changes)
            try:
                require_smoke_relaunch_evidence(invalid, "windows", NATIVE_CONFIG_PROBE_FOV)
            except RuntimeError:
                pass
            else:
                raise AssertionError("Invalid native relaunch evidence accepted")
        for invalid in (
            {"Version": 2, "Scene": {"Camera": {"FieldOfView": 35}}},
            {"Version": 1, "Scene": {"Camera": {"FieldOfView": False}}},
            {"Version": 1, "Scene": {"Camera": {"FieldOfView": 0}}},
        ):
            path.write_text(json.dumps(invalid), encoding="utf-8")
            try:
                read_smoke_configuration(path)
            except RuntimeError:
                pass
            else:
                raise AssertionError("Invalid native configuration accepted")
    require_commit_sha("a" * 40, "test")
    for value in ("a" * 39, "g" * 40, "", None):
        try:
            require_commit_sha(value, "test")
        except RuntimeError:
            pass
        else:
            raise AssertionError("Invalid commit identifier accepted")
    # Smoke latency budget must be visible and stable; a 2-second native
    # teardown is diagnostic, while a >10-second headless hang is a failure.
    assert MAX_HEADLESS_QUIT_TO_EXIT_MS == 10_000
    for allowed in (None, 0, 2_125.0, 10_000.0):
        require_headless_quit_budget(allowed)
    try:
        require_headless_quit_budget(10_001.0)
    except RuntimeError as exc:
        assert "latency regression" in str(exc)
    else:
        raise AssertionError("Expected severe quit latency to fail smoke")
    print("Native Player smoke exit timer self-test PASS")


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        self_test()
        sys.exit(0)
    cli = argparse.ArgumentParser()
    cli.add_argument("--platform", choices=("windows", "windows-nodebug", "macos", "macos-nodebug"), required=True)
    cli.add_argument("--archive", required=True)
    cli.add_argument("--source", required=True)
    cli.add_argument("--pr-head", required=True)
    cli.add_argument("--output", required=True)
    sys.exit(execute(cli.parse_args()))
