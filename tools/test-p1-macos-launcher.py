#!/usr/bin/env python3
"""Cross-platform .app fixture tests for the P1 macOS launcher.

This tests argument validation, a real child process' exit code and fresh
log evidence. The synthetic executable is NOT Unity or AppKit evidence.
"""
import hashlib
import json
import os
from pathlib import Path
import plistlib
import subprocess
import tempfile


ROOT_SCRIPT = Path(__file__).with_name("run-p1-player.sh")
START_MARKER = "VCR inventory: stage=startup category=module id=application status=started"
QUIT_MARKER = "VCR exit: OnApplicationQuit finished"


def run(*args, env):
    return subprocess.run(["bash", str(ROOT_SCRIPT), *map(str, args)],
                          env=env, capture_output=True, text=True, check=False)


def require(value, message):
    if not value:
        raise AssertionError(message)


def main():
    with tempfile.TemporaryDirectory(prefix="vcr-p1-macos-contract-") as folder:
        root = Path(folder)
        app = root / "My example app with spaces.app"
        executable = app / "Contents" / "MacOS" / "Mock Unity Player"
        executable.parent.mkdir(parents=True)
        plist = app / "Contents" / "Info.plist"

        def write_plist(executable_name):
            with plist.open("wb") as handle:
                plistlib.dump({"CFBundleExecutable": executable_name}, handle)

        write_plist(executable.name)
        executable.write_text(
            "#!/usr/bin/env python3\n"
            "import os, pathlib, sys\n"
            "args = sys.argv[1:]\n"
            "pathlib.Path(os.environ['VCR_P1_MAC_MOCK_ARGS']).write_text("
            "'\\n'.join(args) + '\\n', encoding='utf-8')\n"
            "if '-logFile' in args and os.environ.get('VCR_P1_MAC_MOCK_NO_LOG') != '1':\n"
            "    logfile = pathlib.Path(args[args.index('-logFile') + 1])\n"
            "    logfile.write_text(" + repr(START_MARKER + "\n" + QUIT_MARKER + "\n") +
            ", encoding='utf-8')\n"
            "sys.exit(int(os.environ.get('VCR_P1_MAC_MOCK_EXIT', '17')))\n",
            encoding="utf-8",
        )
        executable.chmod(0o755)
        vrm = root / "test model with spaces.vrm"
        vrm.write_text("synthetic", encoding="utf-8")
        config = root / "settings with spaces.json"
        config.write_text("{}", encoding="utf-8")
        args_file = root / "mock argv.txt"
        env = os.environ.copy()
        env["VCR_P1_MAC_MOCK_ARGS"] = str(args_file)
        env["VCR_P1_MAC_MOCK_EXIT"] = "17"

        def reject_before_launch(label, *args):
            args_file.unlink(missing_ok=True)
            result = run(*args, env=env)
            require(result.returncode != 0 and not args_file.exists(),
                    "Allowed " + label + ": " + result.stdout + result.stderr)

        reject_before_launch("missing app",
                             "--app", root / "missing.app", "--evidence-dir",
                             root / "missing-app-evidence")
        reject_before_launch("missing config",
                             "--app", app, "--config", root / "missing.json",
                             "--evidence-dir", root / "missing-config-evidence")
        reject_before_launch("directory config",
                             "--app", app, "--config", root,
                             "--evidence-dir", root / "directory-config-evidence")
        reject_before_launch("missing VRM",
                             "--app", app, "--vrm", root / "missing.vrm",
                             "--evidence-dir", root / "missing-vrm-evidence")
        reject_before_launch("bad mode",
                             "--app", app, "--mode", "invalid",
                             "--evidence-dir", root / "invalid-mode-evidence")
        reject_before_launch("missing option argument", "--app")
        write_plist("../outside")
        reject_before_launch("unsafe executable path",
                             "--app", app, "--evidence-dir",
                             root / "unsafe-bundle-evidence")
        write_plist(executable.name)

        # An actual mock process returning 17 must preserve the exit code.
        # Paths containing spaces survive argument forwarding.
        evidence = root / "fresh evidence folder"
        result = run("--app", app, "--mode", "release",
                     "--vrm", vrm, "--config", config,
                     "--evidence-dir", evidence, env=env)
        require(result.returncode == 17,
                "Native exit 17 was lost: " + result.stdout + result.stderr)
        args = args_file.read_text(encoding="utf-8").splitlines()
        # macOS /var is an alias of /private/var on CI hosts. Compare
        # resolved file identity, not the raw spelling of an absolute path.
        require(len(args) == 4 and args[0].startswith("--vcr-vrm=") and
                args[1].startswith("--vcr-config=") and args[2] == "-logFile" and
                Path(args[0].split("=", 1)[1]).resolve() == vrm.resolve() and
                Path(args[1].split("=", 1)[1]).resolve() == config.resolve() and
                Path(args[3]).resolve() == (evidence / "Player.log").resolve(),
                "Mock argv does not resolve to the expected files: " + repr(args))
        record = json.loads(
            (evidence / "launch-evidence.json").read_text(encoding="utf-8"))
        log = (evidence / "Player.log").read_bytes()
        require(record["platform"] == "macOS" and record["mode"] == "release",
                "Incorrect macOS build metadata")
        require(record["exitCode"] == 17 and record["sourceBound"] is False and
                record["physicalHumanAcceptance"] is False,
                "Incorrect or falsely approved native exit evidence")
        require(record["playerLogBytes"] == len(log) and len(log) > 0 and
                record["playerLogSha256"] == hashlib.sha256(log).hexdigest() and
                record["playerExecutableSha256"] == hashlib.sha256(
                    executable.read_bytes()).hexdigest(),
                "Native process log/executable hashes are inconsistent")
        require(log.index(b"stage=startup") < log.index(b"OnApplicationQuit"),
                "Mock lifecycle markers are missing or reversed")
        reject_before_launch("reused evidence directory",
                             "--app", app, "--evidence-dir", evidence)

        # A 0-exit mock producing no fresh log must still be rejected, and
        # the diagnostic record must never claim operator acceptance.
        env["VCR_P1_MAC_MOCK_EXIT"] = "0"
        env["VCR_P1_MAC_MOCK_NO_LOG"] = "1"
        no_log = root / "new missing log"
        result = run("--app", app, "--evidence-dir", no_log, env=env)
        require(result.returncode != 0, "Missing Player.log was accepted")
        bad_record = json.loads(
            (no_log / "launch-evidence.json").read_text(encoding="utf-8"))
        require(bad_record["exitCode"] == 0 and
                bad_record["playerLogSha256"] is None and
                bad_record["playerLogBytes"] == 0 and
                bad_record["physicalHumanAcceptance"] is False,
                "Missing-log evidence was incorrectly recorded")

    print("P1 macOS launcher native process contract: PASS (synthetic executable only)")


if __name__ == "__main__":
    main()
