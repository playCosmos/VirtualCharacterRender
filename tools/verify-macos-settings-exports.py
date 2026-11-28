#!/usr/bin/env python3
"""Verify every managed P/Invoke declaration against the actual macOS dylib.

The fixture self-test runs on any OS. The real nm check requires macOS and the
project-owned arm64 dylib built by tools/build-native-settings-macos.sh.
This proves exported names only, not P/Invoke calling semantics or GUI use.
"""
import argparse
from pathlib import Path
import re
import subprocess


EXPECTED_API = frozenset({
    "VCR_SettingsCreate",
    "VCR_SettingsShow",
    "VCR_SettingsIsVisible",
    "VCR_SettingsSelectCategory",
    "VCR_SettingsAddControl",
    "VCR_SettingsClearCategory",
    "VCR_SettingsSetText",
    "VCR_SettingsGetText",
    "VCR_SettingsPollAction",
    "VCR_SettingsDestroy",
})
MANAGED_ENTRY = re.compile(
    r'\[\s*DllImport\(\s*"VcrSettingsWindow"\s*,\s*'
    r'EntryPoint\s*=\s*"(VCR_Settings[A-Za-z0-9_]+)"\s*\)\s*\]'
)
NATIVE_EXPORT = re.compile(
    r'^\s*[0-9A-Fa-f]+\s+[Tt]\s+_(VCR_Settings[A-Za-z0-9_]+)\s*$',
    re.MULTILINE,
)


def compare(abi_source: str, symbols: str) -> None:
    declared = MANAGED_ENTRY.findall(abi_source)
    if len(declared) != len(set(declared)):
        raise ValueError("Duplicate managed VcrSettingsWindow EntryPoint declaration")
    managed = set(declared)
    if managed != EXPECTED_API:
        raise ValueError(
            "Managed Settings ABI changed: missing=" +
            repr(sorted(EXPECTED_API - managed)) + " extra=" +
            repr(sorted(managed - EXPECTED_API))
        )
    native = set(NATIVE_EXPORT.findall(symbols))
    missing = managed - native
    if missing:
        raise ValueError("Missing native AppKit Settings exports: " + repr(sorted(missing)))
    # The unmanaged Settings C ABI is deliberately kept as a single, narrow
    # contract; adding new exported bridge functions requires updating tests.
    extra = native - managed
    if extra:
        raise ValueError("Unmatched native AppKit Settings exports: " + repr(sorted(extra)))


def self_test() -> None:
    source = "\n".join(
        '[DllImport("VcrSettingsWindow", EntryPoint = "' + name + '")]'
        for name in sorted(EXPECTED_API)
    )
    native = "\n".join(
        "0000000000001624 T _" + name for name in sorted(EXPECTED_API)
    )
    compare(source, native)
    cases = (
        (source.replace('EntryPoint = "VCR_SettingsCreate"', 'EntryPoint = "VCR_SettingsCreat"'),
         native, "misspelled managed export"),
        (source.replace('[DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsDestroy")]', ""),
         native, "deleted managed bridge declaration"),
        (source + '\n[DllImport("VcrSettingsWindow", EntryPoint = "VCR_SettingsCreate")]',
         native, "duplicate declaration"),
        (source,
         native.replace(" T _VCR_SettingsShow", " T _VCR_SettingsSho"),
         "missing native implementation"),
        (source,
         native + "\n0000000000001624 T _VCR_SettingsUnexpected",
         "unmatched native export"),
        (source, native.replace(" T _VCR_SettingsIsVisible", " t _VCR_SettingsIsVisible")
         .replace(" t _VCR_SettingsIsVisible", " U _VCR_SettingsIsVisible"),
         "undefined native symbol"),
    )
    for candidate_source, candidate_symbols, label in cases:
        try:
            compare(candidate_source, candidate_symbols)
        except ValueError:
            pass
        else:
            raise AssertionError("Invalid Settings ABI accepted: " + label)
    print("P1 macOS Settings export ABI self-test: PASS (synthetic symbols only)")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--source", type=Path)
    parser.add_argument("--binary", type=Path)
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return
    if args.source is None or args.binary is None:
        parser.error("--source and --binary are required for real dylib verification")
    if not args.source.is_file() or not args.binary.is_file():
        parser.error("Settings managed source or compiled native dylib is missing")
    result = subprocess.run(
        ["nm", "-gU", str(args.binary)],
        check=True,
        text=True,
        capture_output=True,
    )
    compare(args.source.read_text(encoding="utf-8"), result.stdout)
    print("P1 macOS Settings real dylib export ABI: PASS (" +
          str(len(EXPECTED_API)) + " C# EntryPoints matched)")


if __name__ == "__main__":
    main()
