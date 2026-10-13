#!/usr/bin/env python3
"""Attribute Unity desktop exit latency using existing Player.log shutdown markers.

Only measures logged managed intervals, not the OS process teardown after the
final log line. Run the Player with -logFile <path>, then pass that path here.
"""
import argparse
from pathlib import Path
import re
import sys

PHASE = re.compile(
    r"VCR shutdown step end: ([^\r\n(]+) \((\d+) ms\)")
MEDIAPIPE = re.compile(
    r"VCR MediaPipe cleanup step end: ([^\r\n(]+) \((\d+) ms\)")
SUMMARY = re.compile(
    r"VCR shutdown: (save finished|scene teardown ended|end) at (\d+) ms")
EXIT = re.compile(
    r"VCR exit: (OnApplicationQuit|OnApplicationQuit finished|runtime bootstrap destroyed) (?:at )?(\d+) ms")
UNIW = re.compile(
    r"VCR UniWinC shutdown: completed \((\d+) ms\)")

HIDE = re.compile(
    r"VCR exit: native window hidden=(True|False) \((\d+) ms\)")

def analyze(contents):
    records = []
    timeline = []
    for line in contents.splitlines():
        for regex, category in ((PHASE, "scene"), (MEDIAPIPE, "mediapipe")):
            match = regex.search(line)
            if match:
                records.append((int(match.group(2)), category, match.group(1).strip()))
        match = UNIW.search(line)
        if match:
            records.append((int(match.group(1)), "native", "UniWinC state reset"))
        match = HIDE.search(line)
        if match:
            records.append((int(match.group(2)), "presentation",
                            "OS window hide (success=" + match.group(1) + ")"))
        match = SUMMARY.search(line)
        if match:
            timeline.append(("runtime " + match.group(1), int(match.group(2))))
        match = EXIT.search(line)
        if match:
            timeline.append((match.group(1), int(match.group(2))))
    records.sort(key=lambda record: -record[0])
    return records, timeline


def format_report(contents, minimum_ms=0):
    records, timeline = analyze(contents)
    lines = ["VCR shutdown latency report (milliseconds)"]
    if "VCR exit: quit requested (t=0 ms)" in contents:
        lines.append("Exit request marker present")
    else:
        lines.append("Exit request marker missing; OS-close path may be uninstrumented")
    for label, ms in timeline[-20:]:
        lines.append(f"  {label}: t={ms} ms")
    lines.append("Measured synchronous cleanup phases (largest first):")
    visible = [(ms, group, label) for ms, group, label in records if ms >= minimum_ms]
    if not visible:
        lines.append("  No measured cleanup phases found")
    for ms, group, label in visible[:30]:
        lines.append(f"  {ms:7d} ms  [{group}] {label}")
    if not records and not timeline:
        lines.append("No VCR shutdown measurements found. Exit the GUI Player normally with a log file.")
    lines.append(
        "Note: overlapping scene/MediaPipe intervals cannot be summed; "
        "time after the last Player.log record requires an external process timer.")
    return "\n".join(lines) + "\n"


def self_test():
    sample = """VCR exit: native window hidden=True (3 ms)
VCR exit: quit requested (t=0 ms)
VCR exit: OnApplicationQuit at 12 ms
VCR MediaPipe cleanup step end: webcam stop (220 ms)
VCR shutdown step end: active operation cancellation (5 ms)
VCR shutdown step end: capability disposal (275 ms)
VCR shutdown: scene teardown ended at 298 ms
VCR shutdown: end at 302 ms, success=True
VCR exit: OnApplicationQuit finished at 315 ms
VCR exit: runtime bootstrap destroyed at 319 ms
"""
    records, timeline = analyze(sample)
    assert records[0] == (275, "scene", "capability disposal")
    assert (3, "presentation", "OS window hide (success=True)") in records
    assert records[1] == (220, "mediapipe", "webcam stop")
    assert ("OnApplicationQuit finished", 315) in timeline
    assert "t=319 ms" in format_report(sample)
    assert "No VCR shutdown measurements" in format_report("Unrelated line")
    print("VCR shutdown log analyzer self-test PASS")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log", type=Path, nargs="?", help="Unity Player.log")
    parser.add_argument("--min-ms", type=int, default=0,
                        help="Hide cleanup phases faster than this threshold")
    parser.add_argument("--self-test", action="store_true")
    options = parser.parse_args()
    if options.self_test:
        self_test()
        return 0
    if options.log is None:
        parser.error("provide Player.log or --self-test")
    if options.min_ms < 0:
        parser.error("--min-ms must be nonnegative")
    try:
        contents = options.log.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        print(f"Cannot read Player.log: {exc}", file=sys.stderr)
        return 1
    print(format_report(contents, options.min_ms), end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
