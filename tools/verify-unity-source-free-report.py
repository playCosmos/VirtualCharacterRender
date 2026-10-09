#!/usr/bin/env python3
"""Verify Unity actually ran the inherited P0-P13 test chain and emitted PASS."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

EXPECTED_SUITE = "P0-P13"
EXPECTED_UNITY = "6000.3.25f1"


def verify(data: object) -> None:
    if not isinstance(data, dict):
        raise ValueError("Unity report must be an object")
    if data.get("suite") != EXPECTED_SUITE:
        raise ValueError("unexpected suite; P13 must execute the inherited P0-P12 chain")
    if data.get("unityVersion") != EXPECTED_UNITY:
        raise ValueError("Unity editor version does not match the pinned version")
    if data.get("passed") is not True:
        raise ValueError("Unity source-free validation did not pass")
    if not isinstance(data.get("timestampUtc"), str) or not data["timestampUtc"].endswith("Z"):
        raise ValueError("missing UTC validation timestamp")


def self_test() -> None:
    good = {
        "suite": EXPECTED_SUITE,
        "unityVersion": EXPECTED_UNITY,
        "passed": True,
        "timestampUtc": "2026-10-09T00:00:00.0000000Z",
    }
    verify(good)
    for corrupted in (
        dict(good, passed=False),
        dict(good, suite="P13"),
        dict(good, unityVersion="6000.0.0f1"),
        dict(good, timestampUtc=""),
    ):
        try:
            verify(corrupted)
        except ValueError:
            pass
        else:
            raise AssertionError("verifier accepted an invalid PASS report")
    print("PASS: Unity P0-P13 evidence verifier self-test")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", nargs="?", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if args.report is None:
        parser.error("report path is required")
    try:
        data = json.loads(args.report.read_text(encoding="utf-8"))
        verify(data)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"FAIL: invalid source-free evidence: {exc}", file=sys.stderr)
        return 1
    print(f"PASS: {EXPECTED_SUITE} on Unity {EXPECTED_UNITY}, {data['timestampUtc']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
