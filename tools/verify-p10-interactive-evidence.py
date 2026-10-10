#!/usr/bin/env python3
"""Validate operator-recorded Windows/macOS GUI + OBS evidence, never infer a visual PASS."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import tempfile

TARGETS = {"720p60": (1280, 720), "1080p60": (1920, 1080)}
PLATFORMS = ("windows", "macos")
CHECKS = (
    "guiStarted", "sceneReady", "outputActive", "overlayCaptureReady",
    "broadcastTargetReady", "transparentOutput", "positiveClientSize",
    "runInBackground", "noNativeOutputError", "obsAlphaRetained",
    "alphaEdgesCorrect", "topmostToggle", "clickThroughToggle",
    "moveResizeDpi", "focusMinimizeRestore", "outputRecovery",
    "shutdownClean",
)
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA64 = re.compile(r"^[0-9a-f]{64}$")
MEDIA_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".mp4", ".mov"}


def empty_case(platform, target):
    w, h = TARGETS[target]
    return {
        "suite": "p10-interactive-capture-v1",
        "platform": platform,
        "target": target,
        "sourceSha": "",
        "playerArchiveSha256": "",
        "unityVersion": "6000.3.25f1",
        "osVersion": "",
        "architecture": "",
        "cpu": "",
        "gpu": "",
        "displayScale": "",
        "obsVersion": "",
        "captureMethod": "",
        "resolution": [w, h],
        "targetFps": 60,
        "frameAverageMs": None,
        "frameP95Ms": None,
        "frameP99Ms": None,
        "obsDroppedFrames": None,
        "obsSkippedFrames": None,
        "checks": {key: None for key in CHECKS},
        "playerLog": "",
        "visualEvidence": "",
        "notes": "",
    }


def init(directory):
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "attachments").mkdir(exist_ok=True)
    for p in PLATFORMS:
        for t in TARGETS:
            path = directory / (p + "-" + t + ".json")
            if path.exists():
                raise ValueError("Will not overwrite recorded evidence: " + str(path))
            path.write_text(json.dumps(empty_case(p, t), indent=2) + "\n", encoding="utf-8")
    print("Created four INCOMPLETE operator templates in " + str(directory))


def artifact(directory, value, allowed_ext=None):
    if not isinstance(value, str) or not value.strip():
        return "path not recorded"
    root = directory.resolve()
    file = (root / value).resolve()
    if not file.is_relative_to(root):
        return "path escapes the evidence directory"
    if allowed_ext and file.suffix.lower() not in allowed_ext:
        return "file must be an image or video"
    if not file.is_file() or file.stat().st_size == 0:
        return "evidence file is missing or empty"
    return None


def inspect_case(directory, platform, target, source):
    path = directory / (platform + "-" + target + ".json")
    errors = []
    try:
        item = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, ValueError) as exc:
        return ["cannot read valid JSON: " + str(exc)]
    if not isinstance(item, dict):
        return ["JSON root must be an object"]
    for key, expected in (
        ("suite", "p10-interactive-capture-v1"),
        ("platform", platform),
        ("target", target),
        ("unityVersion", "6000.3.25f1"),
        ("resolution", list(TARGETS[target])),
        ("targetFps", 60),
    ):
        if item.get(key) != expected or isinstance(item.get(key), bool):
            errors.append(key + " does not match required baseline")
    sha = item.get("sourceSha")
    if not isinstance(sha, str) or not SHA40.fullmatch(sha) or sha != source:
        errors.append("sourceSha missing, invalid or not the specified source commit")
    digest = item.get("playerArchiveSha256")
    if not isinstance(digest, str) or not SHA64.fullmatch(digest):
        errors.append("playerArchiveSha256 must be a full lowercase SHA-256")
    for key in ("osVersion", "cpu", "gpu", "displayScale", "obsVersion", "captureMethod"):
        if not isinstance(item.get(key), str) or not item[key].strip():
            errors.append(key + " is not recorded")
    arch = item.get("architecture")
    if platform == "macos" and arch != "arm64":
        errors.append("macOS evidence must be collected on Apple Silicon (arm64)")
    if platform == "windows" and arch not in ("x86_64", "AMD64"):
        errors.append("Windows evidence must be collected on x64")
    metrics = ("frameAverageMs", "frameP95Ms", "frameP99Ms")
    for key in metrics:
        val = item.get(key)
        if isinstance(val, bool) or not isinstance(val, (float, int)) or not (0 < val < 100000):
            errors.append(key + " must be a measured positive number")
    if all(isinstance(item.get(k), (int, float)) and not isinstance(item.get(k), bool) for k in metrics):
        if not (item["frameAverageMs"] <= item["frameP95Ms"] <= item["frameP99Ms"]):
            errors.append("frame-time percentiles must be ordered average <= P95 <= P99")
    for key in ("obsDroppedFrames", "obsSkippedFrames"):
        val = item.get(key)
        if isinstance(val, bool) or not isinstance(val, int) or val < 0:
            errors.append(key + " must be a recorded nonnegative integer")
    checks = item.get("checks")
    if not isinstance(checks, dict):
        errors.append("checks must be an object")
    else:
        for key in CHECKS:
            if checks.get(key) is not True:
                errors.append("operator has not confirmed " + key)
        for key in checks.keys() - set(CHECKS):
            errors.append("unrecognized check " + key)
    for key, extensions in (("playerLog", None), ("visualEvidence", MEDIA_EXTENSIONS)):
        fault = artifact(directory, item.get(key), extensions)
        if fault:
            errors.append(key + ": " + fault)
    return errors


def verify(directory, source):
    if not SHA40.fullmatch(source):
        raise ValueError("--source must be an exact lowercase 40-digit commit SHA")
    total = 0
    for platform in PLATFORMS:
        for target in TARGETS:
            case = platform + "-" + target
            problems = inspect_case(directory, platform, target, source)
            if problems:
                total += len(problems)
                print(case + ": INCOMPLETE/FAILED")
                for problem in problems:
                    print("  - " + problem)
            else:
                print(case + ": evidence RECORD COMPLETE (operator observations, not independently verified)")
    if total:
        print("P10 evidence NOT ACCEPTED: " + str(total) + " issue(s)")
        return 1
    print("P10 evidence FORMAT COMPLETE; separate visual/hardware review required before P10 PASS.")
    return 0


def self_test():
    source = "a" * 40
    with tempfile.TemporaryDirectory() as tmp:
        directory = Path(tmp)
        init(directory)
        if verify(directory, source) == 0:
            raise AssertionError("empty templates must fail")
        for platform in PLATFORMS:
            for target in TARGETS:
                case = platform + "-" + target
                path = directory / (case + ".json")
                data = json.loads(path.read_text(encoding="utf-8"))
                data.update({
                    "sourceSha": source, "playerArchiveSha256": "b" * 64,
                    "osVersion": "test-os", "architecture": "arm64" if platform == "macos" else "AMD64",
                    "cpu": "test-cpu", "gpu": "test-gpu", "displayScale": "100%",
                    "obsVersion": "test-obs", "captureMethod": "test-method",
                    "frameAverageMs": 10.0, "frameP95Ms": 12.0, "frameP99Ms": 15.0,
                    "obsDroppedFrames": 0, "obsSkippedFrames": 0,
                    "checks": dict.fromkeys(CHECKS, True),
                    "playerLog": "attachments/" + case + ".log",
                    "visualEvidence": "attachments/" + case + ".png",
                })
                for asset in ("playerLog", "visualEvidence"):
                    (directory / data[asset]).write_bytes(b"synthetic-self-test-fixture")
                path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        if verify(directory, source) != 0:
            raise AssertionError("synthetic complete fixture must satisfy format checker")
        path = directory / "macos-720p60.json"
        bad = json.loads(path.read_text())
        bad["checks"]["obsAlphaRetained"] = False
        path.write_text(json.dumps(bad), encoding="utf-8")
        if verify(directory, source) == 0:
            raise AssertionError("negative visual check must fail")
        bad["checks"]["obsAlphaRetained"] = True
        bad["visualEvidence"] = "../escape.png"
        path.write_text(json.dumps(bad), encoding="utf-8")
        if verify(directory, source) == 0:
            raise AssertionError("evidence path escaping directory must fail")
    print("P10 evidence schema self-test PASS")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    group = p.add_mutually_exclusive_group(required=True)
    group.add_argument("--init", type=Path, metavar="DIR")
    group.add_argument("--verify", type=Path, metavar="DIR")
    group.add_argument("--self-test", action="store_true")
    p.add_argument("--source", help="40-character source SHA for --verify")
    args = p.parse_args()
    try:
        if args.self_test:
            self_test()
            return 0
        if args.init:
            init(args.init)
            return 0
        if not args.source:
            p.error("--source is required with --verify")
        return verify(args.verify, args.source)
    except (OSError, ValueError) as exc:
        print("P10 evidence error: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
