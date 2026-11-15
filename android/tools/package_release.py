#!/usr/bin/env python3
"""Package an already tested Android release; never builds or signs an APK.

Python 3.10+, standard library only. Run after the source tree and report are final:
  python tools/package_release.py --apk ABSOLUTE_APK --report FINAL_JSON --out OUTPUTS
Existing release bundles are protected unless --overwrite is supplied explicitly.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import zipfile


VERSION = "0.3.7"
PREFIX = f"SwirStreamAI_Android_{VERSION}"
APK_NAME = f"{PREFIX}.apk"
EXCLUDED_DIRS = {
    "build", ".gradle", ".git", ".idea", ".kotlin", "__pycache__",
    ".venv", "venv", "node_modules", "outputs", ".android", ".cxx",
}
EXCLUDED_FILES = {"local.properties", ".ds_store", "thumbs.db", ".env"}
EXCLUDED_SUFFIXES = {
    ".jks", ".keystore", ".p12", ".pfx", ".key", ".pem", ".pk8",
    ".apk", ".aab", ".apks", ".log", ".pyc", ".class", ".hprof",
}
SENSITIVE_NAME = re.compile(
    r"password|passwd|credential|keystore|signing|private[-_.]?key", re.IGNORECASE
)
USER_DOCS = (
    "START_PL.txt", "README_ANDROID.md", "PORTALE_PRZYKLAD.txt", "MODEL_CARD_ANDROID.md",
    "THIRD_PARTY_ANDROID.md", "LICENSE_APP_MIT.txt", "ICON_PROVENANCE.txt",
    "license-provenance.json", "dependency-inventory.json",
    "release-runtime-classpath.txt",
)
CHUNK = 1024 * 1024


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(CHUNK), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_runtime_match(actual: dict[str, str], expected: dict[str, str]) -> None:
    missing = sorted(expected.keys() - actual.keys())
    extra = sorted(actual.keys() - expected.keys())
    changed = sorted(name for name in expected.keys() & actual.keys() if actual[name] != expected[name])
    if missing or extra or changed:
        raise ValueError(
            "Runtime sources do not match the tested final report: "
            f"missing={missing[:8]}, extra={extra[:8]}, changed={changed[:8]}"
        )


def verified_runtime_hashes(root: Path, report: dict) -> dict[str, str]:
    recorded = report.get("sourceRuntimeSha256AtReportTime")
    if not isinstance(recorded, dict) or not recorded:
        raise ValueError("Final report has no nonempty sourceRuntimeSha256AtReportTime map")
    for name, digest in recorded.items():
        if (
            not isinstance(name, str) or not name.startswith("app/src/main/")
            or not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest)
        ):
            raise ValueError("Final report has an invalid runtime source path or SHA-256")
        safe_member(name)
    runtime = root / "app/src/main"
    if not runtime.is_dir():
        raise ValueError("Runtime source directory is missing")
    actual: dict[str, str] = {}
    for path in [runtime, *sorted(runtime.rglob("*"))]:
        if path.is_symlink() or getattr(path, "is_junction", lambda: False)():
            raise ValueError(f"Runtime source link is not allowed: {path}")
        if not path.resolve().is_relative_to(root):
            raise ValueError(f"Runtime source leaves the project: {path}")
        if path.is_file():
            actual[path.relative_to(root).as_posix()] = sha256(path)
    require_runtime_match(actual, recorded)
    return recorded


def excluded_file(path: Path) -> bool:
    name = path.name.lower()
    return (
        name in EXCLUDED_FILES
        or name.startswith(".env.")
        or path.suffix.lower() in EXCLUDED_SUFFIXES
        or bool(SENSITIVE_NAME.search(name))
    )


def source_files(root: Path, output: Path) -> tuple[list[Path], int]:
    result: list[Path] = []
    skipped = 0
    for current, dirs, files in os.walk(root, followlinks=False):
        directory = Path(current)
        kept_dirs = []
        for name in sorted(dirs):
            candidate = directory / name
            if (
                name.lower() in EXCLUDED_DIRS
                or SENSITIVE_NAME.search(name)
                or candidate.resolve() == output
            ):
                skipped += 1
                continue
            if candidate.is_symlink() or getattr(candidate, "is_junction", lambda: False)():
                raise ValueError(f"Source directory link is not allowed: {candidate}")
            kept_dirs.append(name)
        dirs[:] = kept_dirs
        for name in sorted(files):
            candidate = directory / name
            if excluded_file(candidate):
                skipped += 1
                continue
            if candidate.is_symlink():
                raise ValueError(f"Source file link is not allowed: {candidate}")
            if not candidate.resolve().is_relative_to(root):
                raise ValueError(f"Source file leaves the project: {candidate}")
            result.append(candidate)
    return sorted(result), skipped


def safe_member(name: str) -> str:
    if (
        not name or name.startswith("/") or "\\" in name or ":" in name
        or any(part in {"", ".", ".."} for part in name.split("/"))
        or any(ord(char) < 32 for char in name)
    ):
        raise ValueError(f"Unsafe ZIP member: {name!r}")
    return name


def add_file(archive: zipfile.ZipFile, name: str, path: Path) -> tuple[str, int]:
    safe_member(name)
    before = path.stat()
    digest = hashlib.sha256()
    count = 0
    entry = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
    entry.compress_type = zipfile.ZIP_DEFLATED
    entry.create_system = 3
    entry.external_attr = (0o100755 if path.name == "gradlew" else 0o100644) << 16
    with path.open("rb") as source, archive.open(entry, "w", force_zip64=True) as target:
        for chunk in iter(lambda: source.read(CHUNK), b""):
            target.write(chunk)
            digest.update(chunk)
            count += len(chunk)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError(f"Input changed during packaging: {path}")
    if count != before.st_size:
        raise ValueError(f"Incomplete input read: {path}")
    return digest.hexdigest(), count


def add_bytes(archive: zipfile.ZipFile, name: str, value: bytes) -> tuple[str, int]:
    safe_member(name)
    entry = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
    entry.compress_type = zipfile.ZIP_DEFLATED
    entry.create_system = 3
    entry.external_attr = 0o100644 << 16
    archive.writestr(entry, value)
    return hashlib.sha256(value).hexdigest(), len(value)


def make_bundle(
    destination: Path,
    files: list[tuple[str, Path]],
    extras: list[tuple[str, bytes]],
) -> dict[str, tuple[str, int]]:
    hashes: dict[str, tuple[str, int]] = {}
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name, source in files:
            if name in hashes or name == "SHA256SUMS":
                raise ValueError(f"Duplicate member: {name}")
            hashes[name] = add_file(archive, name, source)
        for name, value in extras:
            if name in hashes or name == "SHA256SUMS":
                raise ValueError(f"Duplicate member: {name}")
            hashes[name] = add_bytes(archive, name, value)
        manifest = "".join(f"{hashes[name][0]}  {name}\n" for name in sorted(hashes)).encode("utf-8")
        hashes["SHA256SUMS"] = add_bytes(archive, "SHA256SUMS", manifest)
    verify_bundle(destination, hashes)
    return hashes


def verify_bundle(path: Path, expected: dict[str, tuple[str, int]]) -> None:
    with zipfile.ZipFile(path, "r") as archive:
        names = archive.namelist()
        if len(names) != len(set(names)) or set(names) != set(expected):
            raise ValueError(f"ZIP member inventory mismatch: {path.name}")
        # Reading every entry through ZipExtFile also verifies its recorded CRC.
        for name in names:
            safe_member(name)
            digest = hashlib.sha256()
            count = 0
            with archive.open(name) as source:
                for chunk in iter(lambda: source.read(CHUNK), b""):
                    digest.update(chunk)
                    count += len(chunk)
            if (digest.hexdigest(), count) != expected[name]:
                raise ValueError(f"ZIP content/hash mismatch: {path.name}/{name}")
        manifest = archive.read("SHA256SUMS").decode("utf-8")
        wanted = "".join(
            f"{expected[name][0]}  {name}\n" for name in sorted(expected) if name != "SHA256SUMS"
        )
        if manifest != wanted:
            raise ValueError(f"ZIP SHA256SUMS mismatch: {path.name}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--overwrite", action="store_true", help="Replace existing output bundles explicitly.")
    args = parser.parse_args()
    if not args.apk.is_absolute():
        raise ValueError("--apk must be an absolute path")
    apk = args.apk.resolve(strict=True)
    report_path = args.report.resolve(strict=True)
    root = Path(__file__).resolve().parent.parent
    output = args.out.resolve()
    if output == root:
        raise ValueError("Output must be separate from the project root")
    if not apk.is_file() or not report_path.is_file():
        raise ValueError("APK and report must be regular files")
    if report_path.stat().st_size > 10 * CHUNK:
        raise ValueError("Final report exceeds 10 MiB")
    report_bytes = report_path.read_bytes()
    report = json.loads(report_bytes.decode("utf-8-sig"))
    if not isinstance(report, dict) or report.get("releaseReady") is not True:
        raise ValueError("Final report must explicitly contain releaseReady: true")
    if report.get("version") != VERSION:
        raise ValueError(f"Final report version must be {VERSION}")
    runtime_hashes = verified_runtime_hashes(root, report)
    recorded_hash = report.get("apkSha256")
    if not isinstance(recorded_hash, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", recorded_hash):
        raise ValueError("Final report has no valid apkSha256")
    apk_hash = sha256(apk)
    if apk_hash != recorded_hash.lower():
        raise ValueError("APK SHA-256 does not match the tested final report")
    with zipfile.ZipFile(apk, "r") as archive:
        if "AndroidManifest.xml" not in archive.namelist() or "classes.dex" not in archive.namelist():
            raise ValueError("Input is not an Android application APK")
        bad_member = archive.testzip()
        if bad_member:
            raise ValueError(f"APK CRC failed: {bad_member}")
    docs = root / "docs"
    user_files = [(APK_NAME, apk)]
    for name in USER_DOCS:
        path = docs / name
        if not path.is_file():
            raise ValueError(f"Required release documentation is missing: {name}")
        user_files.append((f"docs/{name}", path))
    license_files = sorted((docs / "licenses").rglob("*"))
    if not any(path.is_file() for path in license_files):
        raise ValueError("The release must include dependency license files")
    user_files.extend(
        (f"docs/licenses/{path.relative_to(docs / 'licenses').as_posix()}", path)
        for path in license_files if path.is_file()
    )
    project_files, skipped = source_files(root, output)
    source_members = [
        (f"SwirStreamAndroid/{path.relative_to(root).as_posix()}", path) for path in project_files
    ]
    required_source = {
        "gradlew", "gradlew.bat", "gradle/wrapper/gradle-wrapper.jar",
        "app/build.gradle", "docs/MODEL_CARD_ANDROID.md", "tools/package_release.py",
    }
    relative_files = {path.relative_to(root).as_posix() for path in project_files}
    if not required_source.issubset(relative_files):
        raise ValueError(f"Incomplete project source: {sorted(required_source - relative_files)}")
    if not any(name.startswith("docs/dependency-source/") and name.endswith(".zip") for name in relative_files):
        raise ValueError("The source package must include the desugar upstream source archive")
    names = (f"{PREFIX}_PACZKA.zip", f"{PREFIX}_ZRODLA.zip", f"{PREFIX}_artefakty.json")
    output.mkdir(parents=True, exist_ok=True)
    for name in names:
        if (output / name).exists() and not args.overwrite:
            raise FileExistsError(f"Output exists; use --overwrite explicitly: {output / name}")
    with tempfile.TemporaryDirectory(prefix=".swirstream-package-", dir=output) as temporary:
        stage = Path(temporary)
        user_hashes = make_bundle(stage / names[0], user_files, [("WERYFIKACJA.json", report_bytes)])
        if user_hashes[APK_NAME][0] != apk_hash:
            raise ValueError("APK changed after its final-report verification")
        source_hashes = make_bundle(stage / names[1], source_members, [])
        packaged_runtime = {
            name.removeprefix("SwirStreamAndroid/"): value[0]
            for name, value in source_hashes.items()
            if name.startswith("SwirStreamAndroid/app/src/main/")
        }
        require_runtime_match(packaged_runtime, runtime_hashes)
        artifacts = {
            "schema": 1,
            "version": VERSION,
            "releaseReady": True,
            "apkSha256": apk_hash,
            "reportSha256": hashlib.sha256(report_bytes).hexdigest(),
            "sourceFiles": len(project_files),
            "excludedSourceEntries": skipped,
            "archiveMembersVerified": True,
            "verification": "Every ZIP entry was read fully for CRC, byte count and SHA-256; SHA256SUMS was compared to the complete inventory.",
            "artifacts": [
                {"file": APK_NAME, "bytes": apk.stat().st_size, "sha256": apk_hash},
                {"file": names[0], "bytes": (stage / names[0]).stat().st_size,
                 "sha256": sha256(stage / names[0]), "members": len(user_hashes)},
                {"file": names[1], "bytes": (stage / names[1]).stat().st_size,
                 "sha256": sha256(stage / names[1]), "members": len(source_hashes)},
            ],
        }
        (stage / names[2]).write_text(json.dumps(artifacts, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        # Publish only after both complete archives pass verification.
        for name in names:
            os.replace(stage / name, output / name)
    print(json.dumps({"artifactsJson": str(output / names[2]), "bundles": list(names[:2])}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, zipfile.BadZipFile, json.JSONDecodeError) as error:
        print(f"Packaging refused: {error}", file=sys.stderr)
        raise SystemExit(1)
