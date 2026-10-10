#!/usr/bin/env python3
"""Validate operator-recorded Windows/macOS GUI + OBS evidence, never infer a visual PASS."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import sys
import tempfile
import zipfile

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


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for block in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def check_archive(directory, platform, source):
    """Verify the actual CI-produced ZIP against both SHA-256 and source sidecars."""
    if platform not in PLATFORMS:
        raise ValueError("unsupported artifact platform: " + str(platform))
    directory = directory.resolve()
    archive = directory / (platform + ".zip")
    digest_path = directory / (platform + ".sha256")
    source_path = directory / (platform + ".source-sha")
    for path in (archive, digest_path, source_path):
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError("missing/empty artifact: " + str(path))
    if source_path.read_text(encoding="utf-8").strip() != source:
        raise ValueError(platform + " artifact source does not match tested commit")
    digest_tokens = digest_path.read_text(encoding="utf-8").split()
    # GitHub Actions sha256sum records the original runner's absolute workspace
    # path. Only the expected artifact basename/suffix is portable.
    recorded_path = digest_tokens[1].lstrip("*").replace("\\", "/") if len(digest_tokens) == 2 else ""
    expected_suffix = "player-artifacts/" + platform + ".zip"
    if (len(digest_tokens) != 2
            or not SHA64.fullmatch(digest_tokens[0])
            or not (recorded_path == platform + ".zip"
                    or recorded_path == expected_suffix
                    or recorded_path.endswith("/" + expected_suffix))):
        raise ValueError(platform + " artifact .sha256 sidecar is invalid")
    actual = sha256_file(archive)
    if actual != digest_tokens[0]:
        raise ValueError(platform + " archive digest mismatch")
    try:
        with zipfile.ZipFile(archive) as package:
            names = {name.removeprefix("./") for name in package.namelist()}
    except (OSError, zipfile.BadZipFile) as exc:
        raise ValueError(platform + " archive is not a readable ZIP") from exc
    if platform == "windows":
        expected = "VirtualCharacterRender.exe"
        additional = any(name.startswith("VirtualCharacterRender_Data/") and not name.endswith("/")
                         for name in names)
        if expected not in names or not additional:
            raise ValueError("Windows archive lacks Player executable/data")
    else:
        info = "VirtualCharacterRender.app/Contents/Info.plist"
        executable = any(name.startswith("VirtualCharacterRender.app/Contents/MacOS/")
                         and not name.endswith("/") for name in names)
        if info not in names or not executable:
            raise ValueError("macOS archive lacks app Info.plist/executable")
    return actual


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
        "runtimeTelemetry": "",
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



def finite_positive(value):
    return (type(value) in (int, float) and math.isfinite(value)
            and 0 < value < 100000)


def inspect_runtime_telemetry(directory, item, platform, target):
    """Cross-check operator observations with opt-in real Player output.

    This is a data-consistency check, not a visual/OBS PASS and not an
    independent attestation that a screenshot came from this Player.
    """
    errors = []
    fault = artifact(directory, item.get("runtimeTelemetry"))
    if fault:
        return ["runtimeTelemetry: " + fault]
    path = (directory.resolve() / item["runtimeTelemetry"]).resolve()
    if path.stat().st_size > 4 * 1024 * 1024:
        return ["runtimeTelemetry: file exceeds 4 MiB limit"]
    try:
        report = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, ValueError) as exc:
        return ["runtimeTelemetry: invalid JSON: " + str(exc)]
    if not isinstance(report, dict):
        return ["runtimeTelemetry: JSON root must be an object"]

    expected_platform = "WindowsPlayer" if platform == "windows" else "OSXPlayer"
    required = {
        "suite": "p10-interactive-runtime-telemetry-v1",
        "unityVersion": "6000.3.25f1",
        "platform": expected_platform,
        "assurance": "runtime-status-only; not GUI/OBS alpha proof",
        "quitCallbackObserved": True,
    }
    for key, expected in required.items():
        if report.get(key) != expected or (key == "quitCallbackObserved" and type(report.get(key)) is not bool):
            errors.append("runtimeTelemetry: " + key + " does not match expected Player report")
    for key in ("osVersion", "processor", "graphicsDevice", "graphicsApi", "startedAtUtc", "updatedAtUtc"):
        if not isinstance(report.get(key), str) or not report[key].strip():
            errors.append("runtimeTelemetry: " + key + " is missing")
    # GPU info is taken from SystemInfo.graphicsDeviceName in the Player.
    if report.get("graphicsDevice") != item.get("gpu"):
        errors.append("runtimeTelemetry: GPU must match the operator record")

    samples = report.get("samples")
    if not isinstance(samples, list) or len(samples) < 2 or len(samples) > 360:
        return errors + ["runtimeTelemetry: at least two and at most 360 samples required"]
    total = report.get("totalSamples")
    if type(total) is not int or total < len(samples):
        errors.append("runtimeTelemetry: totalSamples must cover every retained sample")
    for i, sample in enumerate(samples):
        if not isinstance(sample, dict) or not finite_positive(sample.get("uptimeSeconds")):
            errors.append("runtimeTelemetry: malformed sample at index " + str(i))
            return errors
        if i and sample["uptimeSeconds"] <= samples[i - 1]["uptimeSeconds"]:
            errors.append("runtimeTelemetry: sample uptimes are not increasing")
            break

    width, height = TARGETS[target]
    tier = "Minimum720p60" if target == "720p60" else "Recommended1080p60"
    # Last two samples must be stable at the requested target and capture-ready.
    # No assumption about real OBS composition, capture quality or hardware.
    for i, sample in enumerate(samples[-2:]):
        label = "runtimeTelemetry: stable sample " + str(i + 1)
        for name, value in (
            ("runtimeStarted", True),
            ("sceneState", "Ready"),
            ("overlayStatusAvailable", True),
            ("overlayState", "Active"),
            ("transparentRequested", True),
            ("overlayCaptureReady", True),
            ("overlayCaptureFailure", "None"),
            ("broadcastTarget", tier),
            ("broadcastCaptureReady", True),
            ("broadcastCaptureFailure", "None"),
            ("runInBackground", True),
            ("requestedWidth", width),
            ("requestedHeight", height),
            ("targetFrameRate", 60),
            ("hasFrameMeasurements", True),
        ):
            if name == "sceneState":
                if sample.get(name) not in ("Ready", "CharacterReady"):
                    errors.append(label + " invalid sceneState")
            elif sample.get(name) != value or (isinstance(value, int) and type(sample.get(name)) is bool):
                errors.append(label + " unexpected " + name)
        if sample.get("sceneError") not in (None, "") or sample.get("overlayError") not in (None, ""):
            errors.append(label + " contains runtime/overlay error")
        if type(sample.get("overlayClientWidth")) is not int or sample["overlayClientWidth"] <= 0:
            errors.append(label + " missing positive overlayClientWidth")
        if type(sample.get("overlayClientHeight")) is not int or sample["overlayClientHeight"] <= 0:
            errors.append(label + " missing positive overlayClientHeight")
        if not isinstance(sample.get("timestampUtc"), str) or not sample["timestampUtc"].strip():
            errors.append(label + " has no timestampUtc")
        times = (sample.get("frameAverageMs"), sample.get("frameP95Ms"), sample.get("frameP99Ms"))
        if not all(finite_positive(v) for v in times) or not times[0] <= times[1] <= times[2]:
            errors.append(label + " invalid frame measurements")
    # Operator must transcribe the LAST stable diagnostics snapshot, rounded
    # to at most one decimal place; compare within floating-point tolerance.
    last = samples[-1]
    for name in ("frameAverageMs", "frameP95Ms", "frameP99Ms"):
        observation = item.get(name)
        native = last.get(name)
        if finite_positive(native) and finite_positive(observation) and abs(native - observation) > 0.11:
            errors.append("runtimeTelemetry: " + name + " differs from latest Player sample")
    return errors


def inspect_case(directory, platform, target, source, archive_sha):
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
    if not isinstance(digest, str) or digest != archive_sha:
        errors.append("playerArchiveSha256 must equal the independently hashed " + platform + " archive")
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
    errors.extend(inspect_runtime_telemetry(directory, item, platform, target))
    return errors


def verify(directory, source, artifacts):
    if not SHA40.fullmatch(source):
        raise ValueError("--source must be an exact lowercase 40-digit commit SHA")
    if not artifacts.is_dir():
        raise ValueError("--artifacts must contain downloaded CI Player packages")
    hashes = {}
    for platform in PLATFORMS:
        try:
            hashes[platform] = check_archive(artifacts, platform, source)
            print(platform + " CI Player artifact: VERIFIED SHA-256 " + hashes[platform])
        except (OSError, UnicodeError, ValueError) as exc:
            print(platform + " CI Player artifact: FAIL - " + str(exc))
    if len(hashes) != len(PLATFORMS):
        print("P10 provenance NOT ACCEPTED; operator records cannot override artifact failure.")
        return 1
    total = 0
    for platform in PLATFORMS:
        for target in TARGETS:
            case = platform + "-" + target
            problems = inspect_case(directory, platform, target, source, hashes[platform])
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
    print("P10 artifact provenance and evidence FORMAT COMPLETE; separate visual/hardware review required.")
    return 0


def self_test():
    source = "a" * 40
    with tempfile.TemporaryDirectory() as tmp:
        directory = Path(tmp) / "evidence"
        artifacts = Path(tmp) / "player-artifacts"
        artifacts.mkdir()
        init(directory)
        digests = {}
        for platform in PLATFORMS:
            archive = artifacts / (platform + ".zip")
            with zipfile.ZipFile(archive, "w") as package:
                if platform == "windows":
                    package.writestr("VirtualCharacterRender.exe", b"synthetic player fixture")
                    package.writestr("VirtualCharacterRender_Data/boot.config", b"test")
                else:
                    package.writestr("VirtualCharacterRender.app/Contents/Info.plist", b"synthetic plist")
                    package.writestr("VirtualCharacterRender.app/Contents/MacOS/VCR", b"test")
            digests[platform] = sha256_file(archive)
            (artifacts / (platform + ".sha256")).write_text(
                digests[platform] + "  player-artifacts/" + platform + ".zip\n", encoding="utf-8")
            (artifacts / (platform + ".source-sha")).write_text(source + "\n", encoding="utf-8")
        # Match the absolute path format emitted by GitHub Actions sha256sum.
        win_digest = artifacts / "windows.sha256"
        win_digest.write_text(digests["windows"] + "  /home/runner/work/project/player-artifacts/windows.zip\n", encoding="utf-8")
        if check_archive(artifacts, "windows", source) != digests["windows"]:
            raise AssertionError("GitHub absolute-path checksum format must pass")
        if verify(directory, source, artifacts) == 0:
            raise AssertionError("empty operator templates must fail with valid archives")
        for platform in PLATFORMS:
            for target in TARGETS:
                case = platform + "-" + target
                path = directory / (case + ".json")
                data = json.loads(path.read_text(encoding="utf-8"))
                data.update({
                    "sourceSha": source, "playerArchiveSha256": digests[platform],
                    "osVersion": "test-os", "architecture": "arm64" if platform == "macos" else "AMD64",
                    "cpu": "test-cpu", "gpu": "test-gpu", "displayScale": "100%",
                    "obsVersion": "test-obs", "captureMethod": "test-method",
                    "frameAverageMs": 10.0, "frameP95Ms": 12.0, "frameP99Ms": 15.0,
                    "obsDroppedFrames": 0, "obsSkippedFrames": 0,
                    "checks": dict.fromkeys(CHECKS, True),
                    "playerLog": "attachments/" + case + ".log",
                    "visualEvidence": "attachments/" + case + ".png",
                    "runtimeTelemetry": "attachments/" + case + "-runtime.json",
                })
                w, h = TARGETS[target]
                kind = "Minimum720p60" if target == "720p60" else "Recommended1080p60"
                def sample(uptime):
                    return {
                        "timestampUtc": "2026-10-10T10:00:00Z",
                        "uptimeSeconds": uptime,
                        "runtimeStarted": True, "sceneState": "Ready",
                        "sceneError": "", "overlayStatusAvailable": True,
                        "overlayState": "Active", "overlayError": "",
                        "overlayClientWidth": w, "overlayClientHeight": h,
                        "transparentRequested": True, "overlayCaptureReady": True,
                        "overlayCaptureFailure": "None", "broadcastTarget": kind,
                        "broadcastCaptureReady": True, "broadcastCaptureFailure": "None",
                        "runInBackground": True, "requestedWidth": w,
                        "requestedHeight": h, "targetFrameRate": 60,
                        "hasFrameMeasurements": True, "frameAverageMs": 10.0,
                        "frameP95Ms": 12.0, "frameP99Ms": 15.0,
                    }
                telemetry = {
                    "suite": "p10-interactive-runtime-telemetry-v1",
                    "assurance": "runtime-status-only; not GUI/OBS alpha proof",
                    "platform": "WindowsPlayer" if platform == "windows" else "OSXPlayer",
                    "unityVersion": "6000.3.25f1", "osVersion": "test-os",
                    "processor": "test-cpu", "graphicsDevice": "test-gpu",
                    "graphicsApi": "test-api", "startedAtUtc": "test-start",
                    "updatedAtUtc": "test-end", "quitCallbackObserved": True,
                    "totalSamples": 2, "samples": [sample(7.0), sample(12.0)],
                }
                (directory / data["runtimeTelemetry"]).write_text(
                    json.dumps(telemetry), encoding="utf-8")
                for field in ("playerLog", "visualEvidence"):
                    (directory / data[field]).write_bytes(b"synthetic-self-test-fixture")
                path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        if verify(directory, source, artifacts) != 0:
            raise AssertionError("synthetic fixture with matching real ZIP digest must pass format check")
        path = directory / "macos-720p60.json"
        good = json.loads(path.read_text())
        for field, wrong in (
            ("playerArchiveSha256", "b" * 64),
            ("visualEvidence", "../escape.png"),
        ):
            bad = dict(good)
            bad[field] = wrong
            path.write_text(json.dumps(bad), encoding="utf-8")
            if verify(directory, source, artifacts) == 0:
                raise AssertionError(field + " negative case must fail")
        bad = dict(good)
        bad["checks"] = dict(good["checks"], obsAlphaRetained=False)
        path.write_text(json.dumps(bad), encoding="utf-8")
        if verify(directory, source, artifacts) == 0:
            raise AssertionError("negative operator check must fail")
        path.write_text(json.dumps(good), encoding="utf-8")
        telemetry_path = directory / good["runtimeTelemetry"]
        original_telemetry = json.loads(telemetry_path.read_text(encoding="utf-8"))
        for mutation in ("wrongPlatform", "overlayNotReady", "missingFrames", "wrongTarget", "notQuit", "metricMismatch"):
            bad_report = json.loads(json.dumps(original_telemetry))
            if mutation == "wrongPlatform":
                bad_report["platform"] = "WindowsPlayer"
            elif mutation == "overlayNotReady":
                bad_report["samples"][-1]["overlayCaptureReady"] = False
            elif mutation == "missingFrames":
                bad_report["samples"][-1]["hasFrameMeasurements"] = False
            elif mutation == "wrongTarget":
                bad_report["samples"][-1]["requestedWidth"] = 1920
            elif mutation == "notQuit":
                bad_report["quitCallbackObserved"] = False
            elif mutation == "metricMismatch":
                bad_report["samples"][-1]["frameAverageMs"] = 8.0
            telemetry_path.write_text(json.dumps(bad_report), encoding="utf-8")
            if verify(directory, source, artifacts) == 0:
                raise AssertionError("runtime telemetry mutation must fail: " + mutation)
        telemetry_path.write_text(json.dumps(original_telemetry), encoding="utf-8")
        if verify(directory, source, artifacts) != 0:
            raise AssertionError("restored runtime telemetry must pass")
        mac_digest = artifacts / "macos.sha256"
        mac_digest.write_text("b" * 64 + "  player-artifacts/macos.zip\n", encoding="utf-8")
        if verify(directory, source, artifacts) == 0:
            raise AssertionError("tampered archive hash sidecar must fail")
        mac_digest.write_text(digests["macos"] + "  player-artifacts/macos.zip\n", encoding="utf-8")
        mac_source = artifacts / "macos.source-sha"
        mac_source.write_text("b" * 40 + "\n", encoding="utf-8")
        if verify(directory, source, artifacts) == 0:
            raise AssertionError("wrong source commit must fail")
        mac_source.write_text(source + "\n", encoding="utf-8")
        with (artifacts / "macos.zip").open("ab") as file:
            file.write(b"altered-binary")
        if verify(directory, source, artifacts) == 0:
            raise AssertionError("modified archive bytes must fail")
    print("P10 archive provenance and evidence schema self-test PASS")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    group = p.add_mutually_exclusive_group(required=True)
    group.add_argument("--init", type=Path, metavar="DIR")
    group.add_argument("--verify", type=Path, metavar="DIR")
    group.add_argument("--check-archive", action="store_true", help="Verify a single downloaded Player ZIP")
    group.add_argument("--self-test", action="store_true")
    p.add_argument("--source", help="Exact 40-character source commit SHA")
    p.add_argument("--artifacts", type=Path, help="Directory with windows.zip/macos.zip and original CI sidecars")
    p.add_argument("--platform", choices=PLATFORMS, help="Required with --check-archive")
    args = p.parse_args()
    try:
        if args.self_test:
            self_test()
            return 0
        if args.init:
            init(args.init)
            return 0
        if not args.source or not SHA40.fullmatch(args.source):
            p.error("--source must be an exact lowercase 40-character SHA")
        if not args.artifacts:
            p.error("--artifacts is required with --verify and --check-archive")
        if args.check_archive:
            if not args.platform:
                p.error("--platform is required with --check-archive")
            digest = check_archive(args.artifacts, args.platform, args.source)
            print(args.platform + " Player archive VERIFIED: " + digest)
            return 0
        return verify(args.verify, args.source, args.artifacts)
    except (OSError, ValueError) as exc:
        print("P10 evidence error: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
