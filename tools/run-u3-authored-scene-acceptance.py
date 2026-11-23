#!/usr/bin/env python3
"""Run U3 authored Alpha/P11 Editor validation with source-bound read-only evidence.

This tests Editor scene ownership/serialization, NOT visual parity, OBS,
native Player, input devices, or human operator acceptance. It never calls
scene generation or a save method. It rejects missing/changed source assets.
"""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from unittest.mock import patch


EXPECTED_EDITOR_VERSION = "6000.3.25f1"
METHOD = "VCR.Editor.Alpha.U3ToolkitSceneValidation.ValidateExistingAlphaAndP11ScenesAndExit"
PASS_MARKER = "VCR U3 authored Alpha/P11 Toolkit scenes: PASS (read only)."
TRACKED = tuple(
    candidate for item in (
        "Assets/VCR/Alpha/AlphaRuntime.unity",
        "Assets/VCR/P11/P11Runtime.unity",
        "Assets/VCR/Alpha/AlphaWorkspacePanelSettings.asset",
        "Assets/VCR/P11/P11WorkspacePanelSettings.asset",
    ) for candidate in (item, item + ".meta")
)


def file_digest(path):
    state = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            state.update(block)
    return state.hexdigest()


def inventory(project):
    evidence = {}
    for relative in TRACKED:
        path = project / relative
        if not path.is_file() or path.is_symlink():
            raise ValueError("Required scene/panel file or .meta missing/linked: " + relative)
        evidence[relative] = {
            "sha256": file_digest(path),
            "bytes": path.stat().st_size,
        }
    return evidence


def decide(before, after, code, log_text, timed_out, process_error):
    return (bool(before) and before == after and code == 0 and
            PASS_MARKER in log_text and not timed_out and
            not process_error)


def self_test():
    with tempfile.TemporaryDirectory(prefix="vcr-u3-authored-test-") as tmp:
        project = Path(tmp)
        for number, relative in enumerate(TRACKED):
            path = project / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(("fixture:" + str(number)).encode("utf-8"))
        original = inventory(project)
        assert decide(original, inventory(project), 0, PASS_MARKER, False, "")
        assert not decide(original, inventory(project), 0, "", False, "")
        assert not decide(original, inventory(project), 1, PASS_MARKER, False, "")
        assert not decide(original, inventory(project), 0, PASS_MARKER, True, "")
        assert not decide(original, inventory(project), 0, PASS_MARKER, False,
                          "Editor invocation failed")
        scene = project / TRACKED[0]
        old = scene.read_bytes()
        scene.write_bytes(bytes([old[0] ^ 1]) + old[1:])
        assert not decide(original, inventory(project), 0, PASS_MARKER,
                          False, "")
        scene.write_bytes(old)
        meta = project / TRACKED[-1]
        meta.unlink()
        try:
            inventory(project)
        except ValueError as exc:
            assert "missing/linked" in str(exc)
        else:
            raise AssertionError("Missing authored .meta was accepted")

    # Integration contract: exercise the actual launcher/report path using
    # a fake Editor subprocess, never a generated Unity fixture as proof
    # that any operator-authored scene has been accepted.
    with tempfile.TemporaryDirectory(prefix="vcr-u3-editor-launch-") as tmp:
        root = Path(tmp)
        project = root / "unity"
        evidence_dir = root / "evidence"
        editor = root / "mock-Unity-editor"
        editor.write_bytes(b"fake-executable-placeholder")
        version = project / "ProjectSettings" / "ProjectVersion.txt"
        version.parent.mkdir(parents=True)
        version.write_text("m_EditorVersion: " + EXPECTED_EDITOR_VERSION + "\n",
                           encoding="utf-8")
        for number, relative in enumerate(TRACKED):
            target = project / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(("test-data-" + str(number)).encode("utf-8"))

        args = argparse.Namespace(project=project, editor=editor,
                                  output=evidence_dir, timeout_seconds=60)

        def editor_response(cmd, *, marker=PASS_MARKER, exit_code=0,
                            mutate=False, simulate_timeout=False, **kwargs):
            assert cmd[0] == str(editor.resolve())
            assert cmd[1:4] == ["-batchmode", "-nographics", "-quit"]
            assert cmd[cmd.index("-executeMethod") + 1] == METHOD
            assert cmd[cmd.index("-projectPath") + 1] == str(project.resolve())
            assert kwargs["cwd"] == str(project.resolve())
            assert kwargs["check"] is False
            assert kwargs["timeout"] == args.timeout_seconds
            if simulate_timeout:
                raise subprocess.TimeoutExpired(cmd, args.timeout_seconds)
            if mutate:
                target = project / TRACKED[0]
                initial = target.read_bytes()
                target.write_bytes(bytes([initial[0] ^ 1]) + initial[1:])
            if marker is not None:
                log = Path(cmd[cmd.index("-logFile") + 1])
                log.write_text("start\n" + marker + "\n", encoding="utf-8")
            return subprocess.CompletedProcess(cmd, exit_code)

        def attempt(**params):
            with patch("subprocess.run",
                       side_effect=lambda cmd, **kwargs: editor_response(
                           cmd, **params, **kwargs)) as invoked:
                status = run(args)
            assert invoked.call_count == 1
            return status, json.loads(
                (evidence_dir / "authored-scene-evidence.json").read_text(
                    encoding="utf-8"))

        status, report = attempt()
        assert status == 0 and report["passed"]
        assert report["explicitPassMarkerObserved"]
        assert report["assetBytesUnchanged"]
        assert report["editorExitCode"] == 0
        assert not report["physicalOperatorAccepted"]
        assert not report["obsOrDeviceAccepted"]
        # The previous PASS is deleted and cannot be recycled.
        status, report = attempt(marker=None)
        assert status == 1 and not report["passed"]
        assert not report["explicitPassMarkerObserved"]
        status, report = attempt(exit_code=9)
        assert status == 1 and report["editorExitCode"] == 9
        status, report = attempt(mutate=True)
        assert status == 1 and not report["assetBytesUnchanged"]
        assert report["explicitPassMarkerObserved"]
        original = project / TRACKED[0]
        original.write_bytes(b"test-data-0")
        status, report = attempt(simulate_timeout=True)
        assert status == 1 and report["timedOut"]
        assert not report["passed"]
        # A bad Unity pin must reject without starting any Editor process.
        version.write_text("m_EditorVersion: 6000.3.24f1\n", encoding="utf-8")
        with patch("subprocess.run") as invoked:
            assert run(args) == 1
            invoked.assert_not_called()
        # Artifacts must never be emitted into the Unity project itself.
        args.output = project / "acceptance-logs"
        try:
            run(args)
        except ValueError as error:
            assert "outside the Unity project" in str(error)
        else:
            raise AssertionError("Evidence written into authored project")
    print("U3 authored-scene read-only evidence and mock Editor integration self-test: PASS")


def run(args):
    project = args.project.resolve()
    editor = args.editor.resolve()
    output = args.output.resolve()
    if not project.is_dir() or not (project / "Assets").is_dir():
        raise ValueError("Unity project/Assets directory missing")
    if not editor.is_file():
        raise ValueError("Unity Editor executable missing")
    # Keep all logs and evidence outside the Editor-controlled Unity project.
    if output == project or project in output.parents:
        raise ValueError("Evidence output must be outside the Unity project")
    if args.output.is_symlink():
        raise ValueError("Evidence output directory cannot be a symlink")
    output.mkdir(parents=True, exist_ok=True)
    report_path = output / "authored-scene-evidence.json"
    log_path = output / "UnityEditor.log"
    stdout_path = output / "editor-stdout.log"
    report = {
        "schema": "vcr-u3-authored-scene-evidence-v1",
        "recordedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "project": str(project),
        "editor": str(editor),
        "editorVersionExpected": EXPECTED_EDITOR_VERSION,
        "method": METHOD,
        "assetHashBefore": {},
        "assetHashAfter": {},
        "assetBytesUnchanged": False,
        "editorExitCode": None,
        "explicitPassMarkerObserved": False,
        "timedOut": False,
        "passed": False,
        "physicalOperatorAccepted": False,
        "obsOrDeviceAccepted": False,
        "error": "",
    }
    started = time.monotonic()
    try:
        version_path = project / "ProjectSettings" / "ProjectVersion.txt"
        if not version_path.is_file():
            raise ValueError("Unity project version file missing")
        if ("m_EditorVersion: " + EXPECTED_EDITOR_VERSION) not in (
                version_path.read_text(encoding="utf-8").splitlines()):
            raise ValueError("Unity project is not pinned to 6000.3.25f1")
        report["projectVersionSha256"] = file_digest(version_path)
        report["assetHashBefore"] = inventory(project)
        # Never allow a stale success from a previous operator run.
        for target in (log_path, stdout_path):
            target.unlink(missing_ok=True)
        command = [
            str(editor), "-batchmode", "-nographics", "-quit",
            "-projectPath", str(project),
            "-executeMethod", METHOD,
            "-logFile", str(log_path),
        ]
        with stdout_path.open("wb") as stream:
            try:
                completed = subprocess.run(
                    command, cwd=str(project), stdout=stream,
                    stderr=subprocess.STDOUT, timeout=args.timeout_seconds,
                    check=False)
                report["editorExitCode"] = completed.returncode
            except subprocess.TimeoutExpired:
                report["timedOut"] = True
                report["error"] = "Unity Editor acceptance timed out"
        if log_path.is_file():
            log = log_path.read_text(encoding="utf-8", errors="replace")
        else:
            log = ""
        report["explicitPassMarkerObserved"] = PASS_MARKER in log
        report["assetHashAfter"] = inventory(project)
        report["assetBytesUnchanged"] = (
            report["assetHashBefore"] == report["assetHashAfter"])
        report["passed"] = decide(
            report["assetHashBefore"], report["assetHashAfter"],
            report["editorExitCode"], log, report["timedOut"], report["error"])
        if not report["passed"] and not report["error"]:
            report["error"] = (
                "No explicit Editor PASS, nonzero exit or authored asset changes")
    except (OSError, ValueError, subprocess.SubprocessError) as exc:
        report["error"] = str(exc)
    finally:
        # This report is never an approval of physical UI/OBS or devices.
        report["elapsedSeconds"] = round(time.monotonic() - started, 2)
        report_path.write_text(
            json.dumps(report, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8")
    print("U3 authored-scene read-only Editor gate: " +
          ("PASS" if report["passed"] else "FAIL"))
    print("Evidence: " + str(report_path))
    if not report["passed"]:
        print("Reason: " + report["error"], file=sys.stderr)
    return 0 if report["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--editor", type=Path)
    parser.add_argument("--project", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--timeout-seconds", type=int, default=900)
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if not all((args.editor, args.project, args.output)):
        parser.error("--editor, --project and --output are required")
    if not 60 <= args.timeout_seconds <= 3600:
        parser.error("Timeout must be between 60 and 3600 seconds")
    try:
        return run(args)
    except (OSError, ValueError) as exc:
        parser.error(str(exc))


if __name__ == "__main__":
    sys.exit(main())
