#!/usr/bin/env python3
"""Repeat the native Player startup/shutdown smoke and summarise its timings.

Each run keeps its own evidence directory. Run 01 is also copied to the
output root so existing single-run consumers (release verification, artifact
inspection) keep working. The repeat summary is intentionally diagnostic:
five green runs are still not a substitute for GUI, OBS, hardware, or soak
acceptance.
"""
import argparse
import json
from pathlib import Path
import shutil
import statistics
import subprocess
import sys


ROOT = Path(__file__).resolve().parent.parent
RUNNER = ROOT / "tools" / "run-player-smoke.py"


def timing_stats(values):
    values = [value for value in values if isinstance(value, (int, float))]
    if not values:
        return None
    return {
        "count": len(values),
        "median": round(statistics.median(values), 1),
        "min": min(values),
        "max": max(values),
    }


def main():
    cli = argparse.ArgumentParser()
    cli.add_argument("--platform", choices=("windows", "macos"), required=True)
    cli.add_argument("--archive", required=True)
    cli.add_argument("--source", required=True)
    cli.add_argument("--output", required=True)
    cli.add_argument("--repeat", type=int, default=5)
    cli.add_argument(
        "--require-quit-marker",
        action="store_true",
        help="fail when Player.log does not contain the managed quit marker",
    )
    args = cli.parse_args()
    if args.repeat < 1:
        raise SystemExit("--repeat must be at least 1")

    archive = Path(args.archive).resolve()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    runs = []

    for index in range(1, args.repeat + 1):
        run_dir = output / f"run-{index:02d}"
        if run_dir.exists():
            shutil.rmtree(run_dir)
        run_dir.mkdir(parents=True)
        command = [
            sys.executable,
            str(RUNNER),
            "--platform", args.platform,
            "--archive", str(archive),
            "--source", args.source,
            "--output", str(run_dir),
        ]
        if args.require_quit_marker:
            command.append("--require-quit-marker")
        print(f"Repeat run {index}/{args.repeat}: starting", flush=True)
        completed = subprocess.run(command, check=False)
        evidence_path = run_dir / "runner-evidence.json"
        evidence = {}
        if evidence_path.is_file():
            try:
                evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as exception:
                evidence = {"error": "invalid runner evidence: " + str(exception)}
        else:
            evidence = {"error": "runner evidence missing"}
        run = {
            "index": index,
            "passed": completed.returncode == 0 and evidence.get("passed") is True,
            "returnCode": completed.returncode,
            "exitCode": evidence.get("exitCode"),
            "playerProcessLifetimeMs": evidence.get("playerProcessLifetimeMs"),
            "quitMarkerMs": evidence.get("quitMarkerMs"),
            "quitTailMs": evidence.get("quitTailMs"),
            "error": evidence.get("error", ""),
            "evidenceDir": str(run_dir),
        }
        runs.append(run)
        print(
            f"Repeat run {index}/{args.repeat}: " +
            ("PASS" if run["passed"] else "FAIL") +
            f" (lifetime={run['playerProcessLifetimeMs']} ms, " +
            f"quitTail={run['quitTailMs']} ms, " +
            f"exit={run['exitCode']})",
            flush=True,
        )

        if index == 1:
            for name in (
                "player-smoke.json",
                "runner-evidence.json",
                "player.log",
                "runner.log",
            ):
                source = run_dir / name
                if source.is_file():
                    shutil.copy2(source, output / name)

    # The runner unpacks the archive next to its per-run output. In repeat
    # mode that places a full Player tree inside the artifact directory; it is
    # reproducible from the archive and must not be uploaded or copied.
    for unpacked in output.glob("unpacked-*"):
        if unpacked.is_dir():
            shutil.rmtree(unpacked, ignore_errors=True)

    passed_runs = sum(1 for run in runs if run["passed"])
    summary = {
        "suite": "native-player-smoke-repeat",
        "platform": args.platform,
        "sourceSha": args.source,
        "archive": str(archive),
        "repeatCount": args.repeat,
        "requireQuitMarker": args.require_quit_marker,
        "passed": passed_runs == args.repeat,
        "passedRuns": passed_runs,
        "failedRuns": args.repeat - passed_runs,
        "runs": runs,
        "statistics": {
            "playerProcessLifetimeMs": timing_stats(
                [run["playerProcessLifetimeMs"] for run in runs]),
            "quitMarkerMs": timing_stats(
                [run["quitMarkerMs"] for run in runs]),
            "quitTailMs": timing_stats(
                [run["quitTailMs"] for run in runs]),
        },
    }
    summary_path = output / "repeat-summary.json"
    summary_path.write_text(
        json.dumps(summary, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print("Repeat summary: " + str(summary_path), flush=True)
    for field in ("playerProcessLifetimeMs", "quitMarkerMs", "quitTailMs"):
        stats = summary["statistics"][field]
        if stats:
            print(
                f"{field}: median={stats['median']} ms, " +
                f"range={stats['min']}-{stats['max']} ms",
                flush=True,
            )
    if summary["passed"]:
        print("Native repeat smoke: PASS", flush=True)
    else:
        print(
            f"Native repeat smoke: FAIL ({passed_runs}/{args.repeat} passed)",
            file=sys.stderr,
        )
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
