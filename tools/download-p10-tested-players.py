#!/usr/bin/env python3
"""Download both P10 CI Players only from one successful, source-matched smoke run.

This validates CI build/startup evidence and ZIP bytes, NOT native GUI/OBS alpha.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile


REPO = "playCosmos/VirtualCharacterRender"
WORKFLOW = "Native Windows and macOS Player startup smoke"
REQUIRED_JOBS = {
    "Build native windows Player",
    "Build native macos Player",
    "Execute windows Player on windows-latest",
    "Execute macos Player on macos-15",
}
SHA40 = re.compile(r"^[0-9a-f]{40}$")


def load_verifier():
    path = Path(__file__).with_name("verify-p10-interactive-evidence.py")
    spec = importlib.util.spec_from_file_location("p10_verifier", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def gh(args):
    completed = subprocess.run(
        ["gh", *args], text=True, capture_output=True, check=False)
    if completed.returncode:
        raise ValueError(
            "GitHub CLI failed (" + str(completed.returncode) + "): " +
            completed.stderr.strip()[:1000])
    return completed.stdout


def check_run(data, run_id, source):
    if not isinstance(data, dict):
        raise ValueError("Workflow response is not a JSON object")
    if (type(data.get("id")) is not int or data["id"] != run_id
            or data.get("name") != WORKFLOW
            or data.get("event") != "push"
            or data.get("head_branch") != "develop"
            or data.get("head_sha") != source
            or data.get("status") != "completed"
            or data.get("conclusion") != "success"):
        raise ValueError("Require a successful develop push native smoke run at the exact source SHA (PR merge-ref builds use a different SHA)")
    location = data.get("repository")
    if not isinstance(location, dict) or location.get("full_name") != REPO:
        raise ValueError("Selected run belongs to a different repository")


def check_jobs(data):
    if not isinstance(data, dict) or not isinstance(data.get("jobs"), list):
        raise ValueError("Workflow job response is incomplete")
    if data.get("total_count") != len(data["jobs"]):
        raise ValueError("Workflow jobs require pagination; refusing incomplete verification")
    jobs = data["jobs"]
    found = {}
    for job in jobs:
        if not isinstance(job, dict):
            raise ValueError("Malformed job entry")
        name = job.get("name")
        if name in REQUIRED_JOBS:
            if name in found:
                raise ValueError("Duplicate required workflow job: " + name)
            found[name] = job.get("conclusion")
            if job.get("status") != "completed":
                raise ValueError("Required smoke job is not completed: " + name)
    if set(found) != REQUIRED_JOBS or any(x != "success" for x in found.values()):
        raise ValueError("Native Windows/macOS builds and smoke jobs are not all successful")


def check_artifacts(data, source):
    if not isinstance(data, dict) or not isinstance(data.get("artifacts"), list):
        raise ValueError("Workflow artifacts response is incomplete")
    if data.get("total_count") != len(data["artifacts"]):
        raise ValueError("Workflow artifact listing requires pagination")
    names = {}
    for platform in ("windows", "macos"):
        expected = "player-" + platform + "-" + source
        matches = [artifact for artifact in data["artifacts"]
                   if isinstance(artifact, dict) and artifact.get("name") == expected]
        if (len(matches) != 1 or matches[0].get("expired") is not False
                or type(matches[0].get("size_in_bytes")) is not int
                or matches[0]["size_in_bytes"] <= 0):
            raise ValueError("Required CI Player artifact is missing, expired or ambiguous: " + expected)
        names[platform] = expected
    return names


def fetch_json(client, url):
    try:
        return json.loads(client(["api", url]))
    except (TypeError, json.JSONDecodeError) as exc:
        raise ValueError("GitHub returned invalid JSON for " + url) from exc


def download(run_id, source, output, client=gh):
    if type(run_id) is not int or run_id <= 0 or not SHA40.fullmatch(source):
        raise ValueError("Require a positive workflow run ID and exact lowercase 40-character SHA")

    # Check *both* native Player smoke jobs, not just the high-level workflow status.
    prefix = "repos/" + REPO + "/actions/runs/" + str(run_id)
    run = fetch_json(client, prefix)
    check_run(run, run_id, source)
    check_jobs(fetch_json(client, prefix + "/jobs?per_page=100"))
    names = check_artifacts(
        fetch_json(client, prefix + "/artifacts?per_page=100"), source)

    output = output.resolve()
    if output.exists() or output.is_symlink():
        raise ValueError("Refusing to overwrite existing artifact directory: " + str(output))
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".p10-download-", dir=output.parent))
    try:
        for platform in ("windows", "macos"):
            folder = staging / platform
            folder.mkdir()
            client(["run", "download", str(run_id), "--repo", REPO,
                    "--name", names[platform], "--dir", str(folder)])
            for ext in (".zip", ".sha256", ".source-sha"):
                file = folder / (platform + ext)
                if not file.is_file() or file.is_symlink() or not file.stat().st_size:
                    raise ValueError("Downloaded Player package is incomplete: " + str(file))
                file.rename(staging / file.name)
            if any(folder.iterdir()):
                raise ValueError("Unexpected downloaded Player artifact file(s)")
            folder.rmdir()

        verifier = load_verifier()
        digests = {}
        for platform in ("windows", "macos"):
            digests[platform] = verifier.check_archive(staging, platform, source)
        staging.rename(output)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    for platform, digest in digests.items():
        print(platform + " CI Player VERIFIED: " + digest)
    print("Source SHA:", source)
    print("Successful native build/startup run:", run_id)
    print("Verified packages:", output)
    print("GUI/OBS transparency and visual quality are NOT validated by this step.")
    return 0


def self_test():
    sha = "a" * 40
    run_id = 123456
    run = {
        "id": run_id, "name": WORKFLOW, "head_sha": sha,
        "status": "completed", "conclusion": "success",
        "event": "push", "head_branch": "develop",
        "repository": {"full_name": REPO},
    }
    jobs = {
        "total_count": len(REQUIRED_JOBS),
        "jobs": [{"name": name, "status": "completed", "conclusion": "success"}
                 for name in sorted(REQUIRED_JOBS)],
    }
    artifacts = {
        "total_count": 2,
        "artifacts": [{"name": "player-" + platform + "-" + sha,
                       "size_in_bytes": 200, "expired": False}
                      for platform in ("windows", "macos")],
    }
    check_run(run, run_id, sha)
    check_jobs(jobs)
    check_artifacts(artifacts, sha)
    for bad in [
        dict(run, head_sha="b" * 40),
        dict(run, event="pull_request"),
        dict(run, head_branch="feature/my-pr"),
        dict(run, conclusion="failure"),
        dict(run, repository={"full_name": "someone/else"}),
    ]:
        try:
            check_run(bad, run_id, sha)
        except ValueError:
            pass
        else:
            raise AssertionError("Mismatched workflow run was accepted")
    failed = dict(jobs, jobs=[dict(x, conclusion="failure")
                              if x["name"] == "Execute macos Player on macos-15"
                              else x for x in jobs["jobs"]])
    try:
        check_jobs(failed)
    except ValueError:
        pass
    else:
        raise AssertionError("Failing macOS smoke job was accepted")

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        fixture = root / "fixture"
        fixture.mkdir()
        verifier = load_verifier()
        for platform in ("windows", "macos"):
            file = fixture / (platform + ".zip")
            with zipfile.ZipFile(file, "w") as archive:
                if platform == "windows":
                    archive.writestr("VirtualCharacterRender.exe", "fixture")
                    archive.writestr("VirtualCharacterRender_Data/boot.config", "fixture")
                else:
                    archive.writestr("VirtualCharacterRender.app/Contents/Info.plist", "fixture")
                    archive.writestr("VirtualCharacterRender.app/Contents/MacOS/VCR", "fixture")
            digest = verifier.sha256_file(file)
            (fixture / (platform + ".sha256")).write_text(
                digest + "  player-artifacts/" + platform + ".zip\n", encoding="utf-8")
            (fixture / (platform + ".source-sha")).write_text(sha + "\n", encoding="utf-8")

        def fake_client(args):
            if args[:1] == ["api"]:
                if args[1].endswith("/artifacts?per_page=100"):
                    return json.dumps(artifacts)
                if args[1].endswith("/jobs?per_page=100"):
                    return json.dumps(jobs)
                return json.dumps(run)
            if args[:2] == ["run", "download"]:
                platform = "windows" if "-windows-" in args[args.index("--name") + 1] else "macos"
                folder = Path(args[args.index("--dir") + 1])
                for ext in (".zip", ".sha256", ".source-sha"):
                    shutil.copy2(fixture / (platform + ext), folder / (platform + ext))
                return ""
            raise AssertionError("Unexpected gh invocation")

        destination = root / "player-artifacts"
        download(run_id, sha, destination, fake_client)
        if not all((destination / (platform + ".zip")).is_file()
                   for platform in ("windows", "macos")):
            raise AssertionError("Expected both platform binaries")
        try:
            download(run_id, sha, destination, fake_client)
        except ValueError:
            pass
        else:
            raise AssertionError("Existing download path overwritten")
        damaged = dict(artifacts, artifacts=[dict(artifacts["artifacts"][0]),
                                            dict(artifacts["artifacts"][1], expired=True)])
        try:
            check_artifacts(damaged, sha)
        except ValueError:
            pass
        else:
            raise AssertionError("Expired artifact was accepted")
    print("P10 verified artifact downloader self-test PASS (no external API calls)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--run-id", type=int)
    parser.add_argument("--source")
    parser.add_argument("--output", type=Path, default=Path("player-artifacts"))
    args = parser.parse_args()
    try:
        if args.self_test:
            self_test()
            return 0
        if not args.run_id or not args.source:
            parser.error("--run-id and --source are required")
        return download(args.run_id, args.source, args.output)
    except (OSError, ValueError, subprocess.SubprocessError) as exc:
        print("P10 CI artifact retrieval REJECTED: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
