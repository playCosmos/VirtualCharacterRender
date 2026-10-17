#!/usr/bin/env python3
"""Restore exact, SHA-verified pinned Ultraleap Git LFS objects from OpenUPM.

The source checkout must be the immutable upstream commit with LFS smudging
DISABLED. Only files whose Git LFS SHA256 and size match are restored. This
does not replace C# source, project metadata, or any unknown LFS object.
"""
import argparse
import base64
import hashlib
import io
import json
import pathlib
import re
import tarfile
import urllib.parse
import urllib.request

PACKAGE = "com.ultraleap.tracking"
VERSION = "7.3.0"
REGISTRY = "https://package.openupm.com"
POINTER = re.compile(
    rb"\Aversion https://git-lfs.github.com/spec/v1\r?\n"
    rb"oid sha256:([0-9a-f]{64})\r?\nsize ([0-9]+)\r?\n?\Z")


def load_pointer(path):
    # Do not read a whole genuine binary when scanning for LFS pointers.
    if path.stat().st_size > 1024:
        return None
    match = POINTER.fullmatch(path.read_bytes())
    return (match.group(1).decode("ascii"), int(match.group(2))) if match else None


def validate_distribution(metadata, archive_bytes):
    if metadata.get("name") != PACKAGE or metadata.get("version") != VERSION:
        raise ValueError("OpenUPM metadata has unexpected Ultraleap package/version")
    dist = metadata.get("dist", {})
    url = dist.get("tarball", "")
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != "https" or not parsed.hostname or not (
        parsed.hostname == "openupm.com" or
        parsed.hostname.endswith(".openupm.com")):
        raise ValueError("OpenUPM package must come from HTTPS OpenUPM host")
    integrity = dist.get("integrity", "")
    if integrity.startswith("sha512-"):
        expected = base64.b64decode(integrity[len("sha512-"):], validate=True)
        actual = hashlib.sha512(archive_bytes).digest()
    elif dist.get("shasum"):
        expected = bytes.fromhex(dist["shasum"])
        actual = hashlib.sha1(archive_bytes).digest()
    else:
        raise ValueError("Package has no supported registry checksum")
    if actual != expected:
        raise ValueError("OpenUPM published package checksum mismatch")


def restore_from_archive(root, archive_bytes):
    pointers = {}
    for path in root.rglob("*"):
        if path.is_file():
            pointer = load_pointer(path)
            if pointer:
                pointers[path.relative_to(root).as_posix()] = (path, *pointer)
    if not pointers:
        raise ValueError("Expected Git LFS pointers in immutable pinned source")
    recovered = {}
    with tarfile.open(fileobj=io.BytesIO(archive_bytes), mode="r:gz") as archive:
        for member in archive:
            if not member.isfile():
                continue
            entry = pathlib.PurePosixPath(member.name)
            if entry.is_absolute() or ".." in entry.parts or len(entry.parts) < 2:
                raise ValueError("Invalid OpenUPM archive member path")
            if entry.parts[0] != "package":
                continue
            rel = pathlib.PurePosixPath(*entry.parts[1:]).as_posix()
            if rel not in pointers:
                continue
            if rel in recovered:
                raise ValueError("Duplicate OpenUPM package file: " + rel)
            stream = archive.extractfile(member)
            if stream is None:
                raise ValueError("Unreadable OpenUPM member: " + rel)
            data = stream.read()
            _, digest, size = pointers[rel]
            if len(data) != size or hashlib.sha256(data).hexdigest() != digest:
                raise ValueError("OpenUPM asset differs from pinned Git LFS object: " + rel)
            recovered[rel] = data
    missing = sorted(set(pointers) - set(recovered))
    if missing:
        raise ValueError("OpenUPM lacks %d pinned Ultraleap LFS objects (first: %s)" %
                         (len(missing), missing[0]))
    for rel, data in recovered.items():
        pointers[rel][0].write_bytes(data)
    print("Restored %d byte-verified pinned Ultraleap LFS assets" % len(recovered))


def restore(root):
    manifest = json.loads((root / "package.json").read_text(encoding="utf-8"))
    if manifest.get("name") != PACKAGE or manifest.get("version") != VERSION:
        raise ValueError("Pinned Ultraleap source package/version mismatch")
    request = urllib.request.Request(
        REGISTRY + "/" + PACKAGE + "/" + VERSION,
        headers={"Accept": "application/json", "User-Agent": "VCR-pinned-lfs-restorer/1"})
    with urllib.request.urlopen(request, timeout=45) as response:
        metadata = json.load(response)
    uri = metadata["dist"]["tarball"]
    with urllib.request.urlopen(uri, timeout=180) as response:
        blob = response.read()
    validate_distribution(metadata, blob)
    restore_from_archive(root, blob)


def self_test():
    from tempfile import TemporaryDirectory
    with TemporaryDirectory() as dirname:
        root = pathlib.Path(dirname)
        original = b"\x89PNG\r\n\x1a\noriginal-LFS-test-content"
        pointer = (
            "version https://git-lfs.github.com/spec/v1\n"
            "oid sha256:%s\nsize %d\n" %
            (hashlib.sha256(original).hexdigest(), len(original))).encode()
        path = root / "Core" / "image.png"
        path.parent.mkdir()
        path.write_bytes(pointer)
        def archive_with(data):
            memory = io.BytesIO()
            with tarfile.open(fileobj=memory, mode="w:gz") as archive:
                entry = tarfile.TarInfo("package/Core/image.png")
                entry.size = len(data)
                archive.addfile(entry, io.BytesIO(data))
            return memory.getvalue()
        restore_from_archive(root, archive_with(original))
        assert path.read_bytes() == original
        path.write_bytes(pointer)
        try:
            restore_from_archive(root, archive_with(b"invalid"))
            raise AssertionError("Corrupt LFS replacement was accepted")
        except ValueError as exception:
            assert "differs" in str(exception)
        assert path.read_bytes() == pointer
        pkg = archive_with(original)
        meta = {"name": PACKAGE, "version": VERSION,
                "dist": {"tarball": REGISTRY + "/file.tgz",
                         "integrity": "sha512-" + base64.b64encode(
                             hashlib.sha512(pkg).digest()).decode()}}
        validate_distribution(meta, pkg)
        try:
            validate_distribution(meta, pkg + b"tampered")
            raise AssertionError("Corrupt registry integrity was accepted")
        except ValueError as exception:
            assert "checksum" in str(exception)
    print("Pinned Ultraleap LFS integrity self-test PASS")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("root", type=pathlib.Path, nargs="?")
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()
    if args.self_test:
        self_test()
    elif args.root is None:
        ap.error("provide pinned upstream Packages/Tracking root")
    else:
        restore(args.root)
