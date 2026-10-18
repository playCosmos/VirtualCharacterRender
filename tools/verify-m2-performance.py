#!/usr/bin/env python3
"""Validate locally recorded M2 native frame/memory evidence; never assert OBS/GUI PASS.

The Player's opt-in CSV uses a cumulative 0.25 ms histogram and mono/native
memory counters. This verifier does not authenticate screenshots, real OBS
frames, physical tracking, VRM material quality, or the Player's source SHA.
Those need an independent artifact provenance check and operator review.
"""
import argparse
import csv
import datetime as dt
import json
import math
from pathlib import Path
import sys
import tempfile

HEADER = (
    "utc", "elapsed_seconds", "total_frames", "slow_frames", "overflow_frames",
    "frame_average_ms", "frame_p95_ms", "frame_p99_ms",
    "gc_alloc_last_frame_bytes", "gc_alloc_available",
    "mono_used_bytes", "total_allocated_bytes", "gc_gen0_collections",
    "gc_gen1_collections", "gc_gen2_collections", "render_width", "render_height",
    "target_fps", "character_loaded", "scene_state",
    "overlay_capture_ready", "quit_observed",
)
TARGETS = {"720p60": (1280, 720, 60), "1080p60": (1920, 1080, 60)}
BOOL_FIELDS = ("gc_alloc_available", "character_loaded",
               "overlay_capture_ready", "quit_observed")
INT_FIELDS = (
    "total_frames", "slow_frames", "overflow_frames",
    "gc_alloc_last_frame_bytes", "mono_used_bytes", "total_allocated_bytes",
    "gc_gen0_collections", "gc_gen1_collections", "gc_gen2_collections",
    "render_width", "render_height", "target_fps",
)
FLOAT_FIELDS = ("elapsed_seconds", "frame_average_ms",
                "frame_p95_ms", "frame_p99_ms")
MAX_CSV_BYTES = 24 * 1024 * 1024
MAX_ROWS = 50_000


def require(condition, message):
    if not condition:
        raise ValueError(message)


def measured_float(raw, field):
    try:
        value = float(raw)
    except (TypeError, ValueError, OverflowError) as exc:
        raise ValueError("Invalid measured " + field) from exc
    require(math.isfinite(value) and value >= 0, "Non-finite/negative " + field)
    return value


def measured_integer(raw, field):
    require(isinstance(raw, str) and raw != "" and raw.isascii() and
            raw.isdecimal(), "Invalid measured integer " + field)
    return int(raw)


def parse_row(raw, index):
    require(None not in raw and all(v is not None for v in raw.values()),
            "Malformed CSV row " + str(index))
    data = {"utc": raw["utc"], "scene_state": raw["scene_state"]}
    for field in FLOAT_FIELDS:
        data[field] = measured_float(raw[field], field)
    for field in INT_FIELDS:
        if field == "gc_alloc_last_frame_bytes" and raw[field] == "-1":
            data[field] = -1  # Unavailable ProfilerRecorder sentinel.
        else:
            data[field] = measured_integer(raw[field], field)
    for field in BOOL_FIELDS:
        value = measured_integer(raw[field], field)
        require(value in (0, 1), "Non-boolean recorded " + field)
        data[field] = bool(value)
    try:
        timestamp = dt.datetime.fromisoformat(data["utc"].replace("Z", "+00:00"))
    except (TypeError, ValueError) as exc:
        raise ValueError("Bad UTC time at row " + str(index)) from exc
    require(timestamp.tzinfo is not None and
            timestamp.utcoffset() == dt.timedelta(0),
            "M2 timestamps must be timezone-aware UTC")
    require(data["scene_state"] in (
        "Ready", "CharacterReady", "LoadingCharacter", "Faulted",
        "Suspended", "ShuttingDown", "Stopped", "Unavailable"),
        "Unrecognized runtime scene state")
    require(data["total_frames"] > 0, "Recording has zero measured frames")
    require(0 <= data["overflow_frames"] <= data["slow_frames"] <= data["total_frames"],
            "Invalid slow/overflow frame counters")
    require(data["frame_average_ms"] > 0 and data["frame_p95_ms"] > 0 and
            data["frame_p99_ms"] >= data["frame_p95_ms"],
            "Frame summary or histogram percentile invalid")
    require(data["mono_used_bytes"] >= 0 and data["total_allocated_bytes"] >= 0,
            "Negative managed/native memory counter")
    if data["gc_alloc_available"]:
        require(data["gc_alloc_last_frame_bytes"] >= 0,
                "GC recorder available but value is absent")
    else:
        require(data["gc_alloc_last_frame_bytes"] == -1,
                "Unavailable frame-GC reading must use -1 sentinel")
    require(data["render_width"] >= 0 and data["render_height"] >= 0 and
            data["target_fps"] >= 0, "Invalid output configuration fields")
    return data, timestamp


def validate_session(metadata, platform):
    require(type(metadata) is dict, "Session metadata must be an object")
    require(type(metadata.get("version")) is int and
            metadata["version"] == 1, "M2 metadata schema mismatch")
    require(metadata.get("suite") == "m2-real-desktop-performance-v1",
            "Unknown M2 measurement suite")
    require(metadata.get("assurance") ==
            "runtime-measurements-only; OBS and device acceptance are external",
            "Runtime assurance level mismatch")
    require(metadata.get("platform") ==
            ("WindowsPlayer" if platform == "windows" else "OSXPlayer"),
            "Recorded operating system does not match requested platform")
    require(metadata.get("unityVersion") == "6000.3.25f1",
            "Recorded Unity version mismatch")
    require(type(metadata.get("developmentBuild")) is bool,
            "Build type is not recorded")
    for key in ("operatingSystem", "architecture", "cpu", "gpu",
                "graphicsApi", "graphicsDriver", "startedUtc"):
        require(isinstance(metadata.get(key), str) and metadata[key].strip(),
                "Missing native session metadata: " + key)
    require(type(metadata.get("graphicsMemoryCapacityMb")) is int and
            metadata["graphicsMemoryCapacityMb"] >= 0,
            "Invalid GPU memory-capacity metadata")
    require(metadata.get("histogramBinWidthMs") == 0.25 and
            metadata.get("histogramOverflowAtMs") == 256.0,
            "Unexpected frame histogram resolution/overflow threshold")
    arch = metadata["architecture"].lower()
    require(arch in (("x64", "amd64") if platform == "windows" else ("arm64", "x64")),
            "Unexpected architecture; native macOS requires separately verified Apple Silicon")
    # arm64 is mandatory to promote to the Apple Silicon product gate;
    # macOS x64 measurements are still useful but cannot be hardware acceptance.
    return metadata


def regression_slope(rows, field):
    count = len(rows)
    average_x = sum(r["elapsed_seconds"] for r in rows) / count
    average_y = sum(r[field] for r in rows) / count
    numerator = sum((r["elapsed_seconds"] - average_x) *
                    (r[field] - average_y) for r in rows)
    denominator = sum((r["elapsed_seconds"] - average_x) ** 2 for r in rows)
    if denominator == 0:
        return None
    return round(numerator / denominator * 3600 / (1024 * 1024), 3)


def inspect(csv_path, platform, target, min_duration_seconds=0,
            require_character=False, require_overlay=False):
    require(platform in ("windows", "macos"), "Unsupported M2 platform")
    require(target in TARGETS, "Unsupported M2 target")
    require(csv_path.is_file() and not csv_path.is_symlink(),
            "Missing/unsafe local M2 CSV")
    require(0 < csv_path.stat().st_size <= MAX_CSV_BYTES,
            "M2 CSV is empty or exceeds the bounded size")
    session_path = Path(str(csv_path) + ".session.json")
    require(session_path.is_file() and not session_path.is_symlink() and
            session_path.stat().st_size <= 32 * 1024,
            "M2 session metadata missing or excessive")
    try:
        metadata = json.loads(session_path.read_text(encoding="utf-8"))
    except (UnicodeError, ValueError) as exc:
        raise ValueError("M2 session metadata is not valid JSON") from exc
    validate_session(metadata, platform)
    rows = []
    start_timestamp = None
    prev_timestamp = None
    previous = None
    try:
        with csv_path.open("r", encoding="utf-8-sig", newline="") as source:
            reader = csv.DictReader(source)
            require(tuple(reader.fieldnames or ()) == HEADER,
                    "M2 CSV schema/header mismatch")
            for raw in reader:
                require(len(rows) < MAX_ROWS, "Too many M2 samples")
                sample, timestamp = parse_row(raw, len(rows) + 1)
                if previous is not None:
                    require(sample["elapsed_seconds"] > previous["elapsed_seconds"],
                            "Non-monotonic M2 uptime")
                    require(sample["total_frames"] >= previous["total_frames"],
                            "Frame count reversed")
                    for field in ("slow_frames", "overflow_frames",
                                  "gc_gen0_collections", "gc_gen1_collections",
                                  "gc_gen2_collections"):
                        require(sample[field] >= previous[field],
                                "M2 monotonic counter reversed: " + field)
                    require(timestamp >= prev_timestamp,
                            "UTC sampling timestamps moved backwards")
                    require(sample["elapsed_seconds"] -
                            previous["elapsed_seconds"] <= 60.0,
                            "M2 sampling gap exceeded one minute")
                    require(not previous["quit_observed"],
                            "M2 quit marker occurred before final sample")
                if start_timestamp is None:
                    start_timestamp = timestamp
                prev_timestamp = timestamp
                rows.append(sample)
                previous = sample
    except UnicodeError as exc:
        raise ValueError("M2 CSV is not valid UTF-8") from exc

    require(len(rows) >= 2, "At least two native performance samples required")
    first, last = rows[0], rows[-1]
    require(last["quit_observed"], "Native normal quit marker missing")
    require(last["elapsed_seconds"] >= min_duration_seconds,
            "Insufficient continuous M2 recording duration")
    require(abs((prev_timestamp - start_timestamp).total_seconds() -
                (last["elapsed_seconds"] - first["elapsed_seconds"])) <= 120,
            "Session clock and UTC sampling timestamps disagree")
    w, h, fps = TARGETS[target]
    for row in rows[1:-1]:
        require((row["render_width"], row["render_height"], row["target_fps"])
                == (w, h, fps), "Player did not remain at target resolution/FPS")
        if require_character:
            require(row["character_loaded"],
                    "VRM-mode performance was collected without a loaded character")
        if require_overlay:
            require(row["overlay_capture_ready"],
                    "Overlay not capture-ready during the measured OBS-mode session")
    require(len(rows[1:-1]) >= 1, "No stable interior M2 performance interval")
    # A stable final config is recorded in the penultimate row. The last row
    # is OnApplicationQuit and may legitimately have sceneState=Stopped.
    require(rows[-2]["scene_state"] in ("Ready", "CharacterReady"),
            "Native scene not ready before normal quit")
    total_seconds = last["elapsed_seconds"]
    summary = {
        "suite": "m2-native-performance-verification-v1",
        "status": "NATIVE_RUNTIME_MEASUREMENTS_VALIDATED",
        "releaseAcceptance": False,
        "visualObsCaptureVerified": False,
        "hardwareAcceptance": "DEFERRED",
        "sourceProvenance": "NOT_VERIFIED_BY_THIS_TOOL",
        "platform": platform,
        "target": target,
        "nativeArchitecture": metadata["architecture"],
        "macosAppleSilicon": (platform == "macos" and
                              metadata["architecture"].lower() == "arm64"),
        "sessionSamples": len(rows),
        "durationSeconds": round(total_seconds, 3),
        "totalFrames": last["total_frames"],
        "measuredAverageFps": round(last["total_frames"] / total_seconds, 3),
        "frameMeanMs": last["frame_average_ms"],
        "frameP95Ms": last["frame_p95_ms"],
        "frameP99Ms": last["frame_p99_ms"],
        "slowFrameCount": last["slow_frames"],
        "slowFramePercent": round(100 * last["slow_frames"] /
                                  last["total_frames"], 4),
        "histogramOverflowFrames": last["overflow_frames"],
        "framePercentilesMayBeLowerBounds": last["overflow_frames"] > 0,
        "gcAllocPerFrameAvailableSamples": sum(r["gc_alloc_available"]
                                               for r in rows),
        "monoUsedMemoryMiB": round(last["mono_used_bytes"] / (1024 * 1024), 3),
        "totalAllocatedMemoryMiB": round(
            last["total_allocated_bytes"] / (1024 * 1024), 3),
        "monoUsedMiBPerHourOLS": regression_slope(rows, "mono_used_bytes"),
        "nativeAllocatedMiBPerHourOLS": regression_slope(
            rows, "total_allocated_bytes"),
        "gcGen0Collections": last["gc_gen0_collections"] -
                             first["gc_gen0_collections"],
        "gcGen1Collections": last["gc_gen1_collections"] -
                             first["gc_gen1_collections"],
        "gcGen2Collections": last["gc_gen2_collections"] -
                             first["gc_gen2_collections"],
        "notes": [
            "Frame percentiles are 0.25ms binned whole-session estimates, not raw frame timings.",
            "Slow frames are >16.667ms application frames, NOT OBS dropped frames.",
            "Profiler counters exclude independent OS compositor/OBS/GPU allocation.",
            "Visual alpha, OBS capture latency, VRM fidelity and physical-device behavior need separate real evidence.",
        ],
    }
    return summary


def self_test():
    with tempfile.TemporaryDirectory() as temporary:
        root = Path(temporary)
        csv_path = root / "case.csv"
        session = {
            "version": 1, "suite": "m2-real-desktop-performance-v1",
            "assurance": "runtime-measurements-only; OBS and device acceptance are external",
            "startedUtc": "2026-10-18T10:00:00Z",
            "platform": "WindowsPlayer", "operatingSystem": "Windows test",
            "architecture": "X64", "cpu": "test CPU", "gpu": "test GPU",
            "graphicsApi": "D3D11", "graphicsDriver": "test driver",
            "graphicsMemoryCapacityMb": 4096,
            "unityVersion": "6000.3.25f1", "developmentBuild": False,
            "histogramBinWidthMs": 0.25,
            "histogramOverflowAtMs": 256.0,
        }
        (root / "case.csv.session.json").write_text(
            json.dumps(session), encoding="utf-8")
        start = dt.datetime(2026, 10, 18, 10, 0, tzinfo=dt.timezone.utc)
        def fake_row(index, quit_flag):
            result = {name: "0" for name in HEADER}
            result.update({
                "utc": (start + dt.timedelta(seconds=index * 5)).isoformat(),
                "elapsed_seconds": str((index + 1) * 5),
                "total_frames": str((index + 1) * 300),
                "slow_frames": str(index + 1),
                "overflow_frames": "0",
                "frame_average_ms": "16.1",
                "frame_p95_ms": "16.5",
                "frame_p99_ms": "17.25",
                "gc_alloc_last_frame_bytes": "-1",
                "gc_alloc_available": "0",
                "mono_used_bytes": str(10000000 + index * 10000),
                "total_allocated_bytes": str(20000000 + index * 10000),
                "render_width": "1280", "render_height": "720",
                "target_fps": "60", "character_loaded": "1",
                "scene_state": "Stopped" if quit_flag else "CharacterReady",
                "overlay_capture_ready": "1",
                "quit_observed": "1" if quit_flag else "0",
            })
            return result
        rows = [fake_row(0, False), fake_row(1, False),
                fake_row(2, False), fake_row(3, True)]
        def write(items):
            with csv_path.open("w", encoding="utf-8", newline="") as output:
                writer = csv.DictWriter(output, HEADER)
                writer.writeheader()
                writer.writerows(items)
        write(rows)
        result = inspect(csv_path, "windows", "720p60", require_character=True,
                         require_overlay=True)
        assert result["totalFrames"] == 1200
        assert result["releaseAcceptance"] is False
        assert result["visualObsCaptureVerified"] is False
        assert result["gcAllocPerFrameAvailableSamples"] == 0
        for broken in (
            [*rows[:-1], dict(rows[-1], quit_observed="0")],
            [*rows[:-1], dict(rows[-1], total_frames="1")],
            [*rows[:-1], dict(rows[-1], frame_p99_ms="nan")],
            [*rows[:-1], dict(rows[-1], gc_alloc_available="1")],
            [*rows[:1], dict(rows[1], render_width="1920"), *rows[2:]],
        ):
            write(broken)
            try:
                inspect(csv_path, "windows", "720p60", require_character=True)
            except ValueError:
                pass
            else:
                raise AssertionError("Malformed/false M2 evidence accepted")
        write(rows)
        try:
            inspect(csv_path, "windows", "720p60", min_duration_seconds=14400)
        except ValueError:
            pass
        else:
            raise AssertionError("Synthetic short session accepted as 4h soak")
        try:
            inspect(csv_path, "macos", "720p60")
        except ValueError:
            pass
        else:
            raise AssertionError("Windows session accepted as macOS")
    print("M2 native performance evidence verifier self-test PASS; G/OBS unverified")


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--self-test", action="store_true")
    cli.add_argument("--csv", type=Path)
    cli.add_argument("--platform", choices=("windows", "macos"))
    cli.add_argument("--target", choices=tuple(TARGETS))
    cli.add_argument("--min-duration-seconds", type=int, default=0)
    cli.add_argument("--require-character", action="store_true")
    cli.add_argument("--require-overlay", action="store_true")
    cli.add_argument("--report", type=Path)
    args = cli.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.csv is None or args.platform is None or args.target is None:
        cli.error("--csv, --platform and --target are required")
    try:
        require(0 <= args.min_duration_seconds <= 172800,
                "Invalid duration requirement")
        result = inspect(args.csv, args.platform, args.target,
                         args.min_duration_seconds, args.require_character,
                         args.require_overlay)
        output = json.dumps(result, indent=2, ensure_ascii=False) + "\n"
        if args.report:
            with args.report.open("x", encoding="utf-8") as report:
                report.write(output)
        print(output, end="")
        return 0
    except (OSError, ValueError, OverflowError, csv.Error) as exc:
        print("M2 native evidence INCOMPLETE/INVALID: " + str(exc),
              file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
