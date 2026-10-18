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
EVENT_HEADER = ("utc", "elapsed_seconds", "sequence", "event", "origin")
EVENT_TYPES = {
    "session_started": "recorder",
    "session_end": "recorder",
    "character_loaded": "character.loader",
    "character_unloaded": "character.loader",
    "character_unloaded_shutdown": "character.loader",
    "character_initially_loaded": "character.loader",
    "character_load_failed": "character.loader",
    "tracking_source_lost": "tracking.presence",
    "tracking_source_restored": "tracking.presence",
    "tracking_subject_lost": "tracking.presence",
    "tracking_subject_restored": "tracking.presence",
}
MAX_EVENT_ROWS = 100_000
MAX_EVENT_CSV_BYTES = 24 * 1024 * 1024


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
    try:
        started = dt.datetime.fromisoformat(
            metadata["startedUtc"].replace("Z", "+00:00"))
    except ValueError as exc:
        raise ValueError("M2 session start is not a valid UTC timestamp") from exc
    require(started.tzinfo is not None and
            started.utcoffset() == dt.timedelta(0),
            "M2 session start must be timezone-aware UTC")
    return started


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



def inspect_transitions(csv_path, session_started, last_elapsed,
                        require_events=False, min_character_cycles=0):
    events_path = Path(str(csv_path) + ".events.csv")
    if not events_path.exists():
        require(not events_path.is_symlink(),
                "M2 events sidecar cannot be a dangling symlink")
        require(not require_events and min_character_cycles == 0,
                "M2 lifecycle event evidence is required but missing")
        return None

    require(events_path.is_file() and not events_path.is_symlink() and
            0 < events_path.stat().st_size <= MAX_EVENT_CSV_BYTES,
            "M2 events CSV is empty, unsafe or excessive")
    rows = []
    previous_utc = None
    previous_elapsed = -1.0
    counts = {event_type: 0 for event_type in EVENT_TYPES}
    loaded = False
    complete_cycles = 0
    source_lost = False
    source_return_pairs = 0
    try:
        with events_path.open("r", encoding="utf-8-sig", newline="") as source:
            reader = csv.DictReader(source)
            require(tuple(reader.fieldnames or ()) == EVENT_HEADER,
                    "M2 transition event schema/header mismatch")
            for raw in reader:
                require(len(rows) < MAX_EVENT_ROWS, "M2 event row safety limit exceeded")
                require(None not in raw and all(v is not None for v in raw.values()),
                        "Malformed M2 transition event row")
                event = raw["event"]
                require(event in EVENT_TYPES and raw["origin"] == EVENT_TYPES[event],
                        "Unrecognized or misattributed M2 transition event")
                elapsed = measured_float(raw["elapsed_seconds"], "event elapsed")
                sequence = measured_integer(raw["sequence"], "event sequence")
                require(sequence == len(rows) + 1, "M2 event sequence gap/replay")
                try:
                    timestamp = dt.datetime.fromisoformat(
                        raw["utc"].replace("Z", "+00:00"))
                except ValueError as exc:
                    raise ValueError("Malformed transition event UTC") from exc
                require(timestamp.tzinfo is not None and
                        timestamp.utcoffset() == dt.timedelta(0),
                        "M2 event UTC must be timezone-aware")
                require(elapsed >= previous_elapsed and
                        (previous_utc is None or timestamp >= previous_utc),
                        "M2 event monotonic chronology reversed")
                require(elapsed <= last_elapsed + 10.0,
                        "Transition event outside completed M2 runtime")
                require(abs((timestamp - session_started).total_seconds() -
                            elapsed) <= 120.0,
                        "M2 transition event disagrees with session start UTC")
                if not rows:
                    require(event == "session_started" and elapsed <= 60.0,
                            "M2 transition log missing timely start event")
                else:
                    require(event != "session_started",
                            "M2 event log has duplicate session start")
                    require(rows[-1] != "session_end",
                            "Transition event follows session_end")
                counts[event] += 1
                if event == "character_loaded":
                    loaded = True
                elif event == "character_unloaded":
                    if loaded:
                        complete_cycles += 1
                    loaded = False
                elif event == "character_unloaded_shutdown":
                    loaded = False
                elif event == "tracking_source_lost":
                    source_lost = True
                elif event == "tracking_source_restored":
                    if source_lost:
                        source_return_pairs += 1
                    source_lost = False
                rows.append(event)
                previous_utc = timestamp
                previous_elapsed = elapsed
    except UnicodeError as exc:
        raise ValueError("M2 event CSV must be UTF-8") from exc
    require(len(rows) >= 2 and rows[-1] == "session_end" and
            counts["session_end"] == 1,
            "M2 transition event log missing normal session_end")
    require(complete_cycles >= min_character_cycles,
            "Insufficient observed successful VRM load/unload cycles")
    return {
        "source": "actual-character-loader-callbacks-and-normalized-tracking-presence",
        "eventRows": len(rows),
        "characterLoadedEvents": counts["character_loaded"],
        "characterUnloadedEvents": counts["character_unloaded"],
        "shutdownUnloadEventsExcluded": counts["character_unloaded_shutdown"],
        "characterLoadFailedEvents": counts["character_load_failed"],
        "characterInitiallyLoadedAtRecorderStart": (
            counts["character_initially_loaded"] > 0),
        "successfulLoadUnloadPairs": complete_cycles,
        "trackingSourceLostEvents": counts["tracking_source_lost"],
        "trackingSourceRestoredEvents": counts["tracking_source_restored"],
        "trackingSourceLossReturnPairs": source_return_pairs,
        "trackingSubjectLostEvents": counts["tracking_subject_lost"],
        "trackingSubjectRestoredEvents": counts["tracking_subject_restored"],
        "physicalDeviceReconnectCyclesVerified": False,
    }


def inspect(csv_path, platform, target, min_duration_seconds=0,
            require_character=False, require_overlay=False,
            require_events=False, min_character_cycles=0):
    require(platform in ("windows", "macos"), "Unsupported M2 platform")
    require(0 <= min_character_cycles <= MAX_EVENT_ROWS,
            "Invalid minimum recorded VRM cycle requirement")
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
    session_started = validate_session(metadata, platform)
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
    # All elapsed-time claims must be anchored to the recorder's actual
    # session start, not just internally monotonic rows. Without this guard,
    # a handful of 5-second rows beginning at elapsed=14400 could be
    # misclassified as a continuous 4-hour soak.
    require(first["elapsed_seconds"] <= 60.0,
            "First M2 sample was too late to establish continuity")
    require(abs((start_timestamp - session_started).total_seconds() -
                first["elapsed_seconds"]) <= 10.0,
            "First M2 sample is inconsistent with the session start")
    require(abs((prev_timestamp - session_started).total_seconds() -
                last["elapsed_seconds"]) <= 120.0,
            "Final M2 sample is inconsistent with the session start")
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
    transitions = inspect_transitions(csv_path, session_started, total_seconds,
                                      require_events, min_character_cycles)
    # Sampled scene state is not a lifecycle event log: a five-second
    # interval can miss arbitrarily many short load/connection transitions.
    # Surface anomalies without pretending these are 50 completed cycles.
    interior = rows[1:-1]
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
        "lifecycleTransitionEvidence": transitions,
        "realTrackingReconnectCyclesVerified": False,
        "durationSeconds": round(total_seconds, 3),
        "interiorRuntimeSamples": len(interior),
        "sceneFaultedSamples": sum(r["scene_state"] == "Faulted"
                                   for r in interior),
        "sceneNonReadySamples": sum(
            r["scene_state"] not in ("Ready", "CharacterReady")
            for r in interior),
        "characterLoadedSamples": sum(r["character_loaded"]
                                      for r in interior),
        "overlayCaptureReadySamples": sum(r["overlay_capture_ready"]
                                          for r in interior),
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
            "Five-second scene samples cannot count VRM load/unload or tracking connect/disconnect cycles.",
            "Transition events can count successful loader callbacks; a reload or replacement without unload is not a completed cycle.",
            "Automatic scene shutdown unloads are excluded from successful manual load/unload pairs.",
            "Normalized tracking presence loss/return is NOT independently proven physical/protocol disconnect/reconnect.",
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
        assert result["interiorRuntimeSamples"] == 2
        assert result["characterLoadedSamples"] == 2
        assert result["overlayCaptureReadySamples"] == 2
        assert result["sceneFaultedSamples"] == 0
        assert result["sceneNonReadySamples"] == 0
        assert result["lifecycleTransitionEvidence"] is None
        event_path = Path(str(csv_path) + ".events.csv")
        events = [
            ("2026-10-18T10:00:00Z", "0.000", "1", "session_started", "recorder"),
            ("2026-10-18T10:00:06Z", "6.000", "2", "character_loaded", "character.loader"),
            ("2026-10-18T10:00:07Z", "7.000", "3", "character_loaded", "character.loader"),
            ("2026-10-18T10:00:08Z", "8.000", "4", "character_unloaded", "character.loader"),
            ("2026-10-18T10:00:09Z", "9.000", "5", "tracking_source_lost", "tracking.presence"),
            ("2026-10-18T10:00:10Z", "10.000", "6", "tracking_source_restored", "tracking.presence"),
            ("2026-10-18T10:00:15Z", "15.000", "7", "session_end", "recorder"),
        ]
        def write_events(items):
            with event_path.open("w", encoding="utf-8", newline="") as output:
                writer = csv.writer(output)
                writer.writerow(EVENT_HEADER)
                writer.writerows(items)
        write_events(events)
        measured = inspect(csv_path, "windows", "720p60", require_events=True,
                           min_character_cycles=1)
        transitions = measured["lifecycleTransitionEvidence"]
        assert transitions["successfulLoadUnloadPairs"] == 1
        assert transitions["characterLoadedEvents"] == 2
        assert transitions["characterUnloadedEvents"] == 1
        assert transitions["trackingSourceLossReturnPairs"] == 1
        assert transitions["physicalDeviceReconnectCyclesVerified"] is False
        shutdown_events = [
            events[0],
            events[1],
            ("2026-10-18T10:00:08Z", "8.000", "3",
             "character_unloaded_shutdown", "character.loader"),
            ("2026-10-18T10:00:15Z", "15.000", "4", "session_end", "recorder"),
        ]
        write_events(shutdown_events)
        excluded = inspect(csv_path, "windows", "720p60", require_events=True)
        assert excluded["lifecycleTransitionEvidence"]["successfulLoadUnloadPairs"] == 0
        assert excluded["lifecycleTransitionEvidence"]["shutdownUnloadEventsExcluded"] == 1
        try:
            inspect(csv_path, "windows", "720p60", min_character_cycles=1)
        except ValueError:
            pass
        else:
            raise AssertionError("Automatic shutdown unload incorrectly counted as user cycle")
        write_events(events)
        for broken in (
            events[:-1],
            [*events, events[-1]],
        ):
            write_events(broken)
            try:
                inspect(csv_path, "windows", "720p60", require_events=True)
            except ValueError:
                pass
            else:
                raise AssertionError("Incomplete or duplicated M2 event footer accepted")
        for broken in (
            [*events[:2], (events[2][0], events[2][1], "20",
                           events[2][3], events[2][4]), *events[3:]],
            [*events[:3], (events[3][0], "99999", events[3][2],
                           events[3][3], events[3][4]), *events[4:]],
            [*events[:4], (events[4][0], events[4][1], events[4][2],
                           "tracking_source_lost", "physical.device"), *events[5:]],
        ):
            write_events(broken)
            try:
                inspect(csv_path, "windows", "720p60", require_events=True)
            except ValueError:
                pass
            else:
                raise AssertionError("Fabricated or corrupted M2 transitions accepted")
        write_events(events)
        try:
            inspect(csv_path, "windows", "720p60", min_character_cycles=2)
        except ValueError:
            pass
        else:
            raise AssertionError("Model replacement miscounted as completed load/unload")
        event_path.unlink()
        try:
            inspect(csv_path, "windows", "720p60", require_events=True)
        except ValueError:
            pass
        else:
            raise AssertionError("Required transition events omitted without failure")
        write([rows[0], dict(rows[1], scene_state="Faulted"), *rows[2:]])
        with_fault = inspect(csv_path, "windows", "720p60")
        assert with_fault["sceneFaultedSamples"] == 1
        assert with_fault["sceneNonReadySamples"] == 1
        write(rows)
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
        # An already-advanced uptime must never satisfy a four-hour soak
        # with only a few seconds of actual samples.
        fabricated = [
            dict(row, elapsed_seconds=str(14400 + index * 5))
            for index, row in enumerate(rows)
        ]
        write(fabricated)
        try:
            inspect(csv_path, "windows", "720p60", min_duration_seconds=14400)
        except ValueError:
            pass
        else:
            raise AssertionError("Late-start M2 rows accepted as a 4h soak")
        write(rows)
        session_path = root / "case.csv.session.json"
        session_path.write_text(json.dumps(dict(
            session, startedUtc="2026-10-17T10:00:00Z")), encoding="utf-8")
        try:
            inspect(csv_path, "windows", "720p60")
        except ValueError:
            pass
        else:
            raise AssertionError("Unrelated M2 session timestamp accepted")
        session_path.write_text(json.dumps(session), encoding="utf-8")
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
    cli.add_argument("--require-events", action="store_true",
                     help="Require completed M2 .events.csv lifecycle sidecar")
    cli.add_argument("--min-character-cycles", type=int, default=0,
                     help="Require N observed successful loader load-then-unload pairs; not replacement or physical tracking cycles")
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
                         args.require_overlay, args.require_events,
                         args.min_character_cycles)
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
