#!/usr/bin/env python3
"""Require real Windows IL2CPP runtime evidence before publishing an alpha."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

UNITY_VERSION = "6000.3.25f1"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def read_json(path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exception:
        raise RuntimeError(
            "Invalid JSON evidence " + str(path) + ": " + str(exception)
        ) from exception


def read_key_values(path):
    values = {}
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except OSError as exception:
        raise RuntimeError("Missing IL2CPP evidence " + str(path)) from exception
    for line in lines:
        if not line.strip():
            continue
        key, separator, value = line.partition("=")
        if not separator:
            raise RuntimeError("Malformed IL2CPP evidence line: " + line)
        values[key.strip()] = value.strip()
    return values


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def sidecar(archive, suffix):
    candidates = (
        archive.with_suffix(suffix),
        archive.with_name(archive.name + suffix),
    )
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise RuntimeError(
        "Missing archive sidecar: " +
        " or ".join(str(candidate) for candidate in candidates))


def verify(args):
    smoke = read_json(Path(args.smoke))
    runner = read_json(Path(args.runner))
    evidence = read_key_values(Path(args.evidence))
    archive = Path(args.archive)

    require(archive.is_file(), "Windows archive missing")
    archive_sha = sha256(archive)
    digest_file = sidecar(archive, ".sha256")
    recorded_sha = digest_file.read_text(encoding="utf-8").split()[0].lower()
    require(recorded_sha == archive_sha, "Archive SHA-256 sidecar mismatch")

    require(runner.get("suite") == "native-player-smoke-runner",
            "Unexpected native runner suite")
    require(runner.get("platform") == "windows",
            "Unexpected native runner platform")
    require(runner.get("passed") is True, "Native runner did not pass")
    require(runner.get("error") == "", "Native runner reported an error")
    require(runner.get("exitCode") == 0, "Player exit code was not zero")
    require(runner.get("sourceSha") == args.source,
            "Native runner source SHA mismatch")
    require(runner.get("archiveSha256") == archive_sha,
            "Native runner archive SHA mismatch")
    require(isinstance(runner.get("playerExecutableSha256"), str) and
            len(runner["playerExecutableSha256"]) == 64,
            "Player executable SHA-256 missing")
    require((runner.get("playerProcessLifetimeMs") or 0) > 0,
            "Player process lifetime missing")

    report = runner.get("playerReport") or {}
    for field in ("started", "sceneReady", "noCharacter", "stopped", "passed"):
        require(report.get(field) is True,
                "Native runner Player report did not confirm " + field)

    require(smoke.get("suite") == "player-startup-shutdown",
            "Unexpected Player smoke suite")
    require(smoke.get("platform") == "WindowsPlayer",
            "Expected a native Windows Player")
    require(smoke.get("unityVersion") == UNITY_VERSION,
            "Player Unity version mismatch")
    for field in ("started", "sceneReady", "noCharacter", "stopped", "passed"):
        require(smoke.get(field) is True,
                "Player did not confirm " + field)
    require(smoke.get("error") == "", "Player reported an error")

    require(evidence.get("backend") == "IL2CPP",
            "Windows player was not built with IL2CPP")
    require(evidence.get("version") == args.version,
            "IL2CPP evidence version mismatch")
    require(evidence.get("source_sha") == args.source,
            "IL2CPP evidence source SHA mismatch")
    require(evidence.get("unity_version") == UNITY_VERSION,
            "IL2CPP evidence Unity version mismatch")
    require(len(evidence.get("gameassembly_sha256", "")) == 64,
            "GameAssembly.dll SHA-256 missing")
    require(int(evidence.get("gameassembly_size", "0")) > 0,
            "GameAssembly.dll size missing")
    require(len(evidence.get("unityplayer_sha256", "")) == 64,
            "UnityPlayer.dll SHA-256 missing")
    require(len(evidence.get("global_metadata_sha256", "")) == 64,
            "global-metadata.dat SHA-256 missing")

    print("Windows IL2CPP release evidence: PASS")
    print("archive_sha256=" + archive_sha)
    print("gameassembly_size=" + evidence["gameassembly_size"])
    print("player_exit=" + str(runner["exitCode"]))
    print("player_lifetime_ms=" + str(runner["playerProcessLifetimeMs"]))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", required=True)
    parser.add_argument("--runner", required=True)
    parser.add_argument("--evidence", required=True)
    parser.add_argument("--archive", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    try:
        verify(args)
    except Exception as exception:
        print("Windows IL2CPP release evidence: FAIL: " + str(exception),
              file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
