"""Copy encrypted local backups to an explicitly selected directory; never delete backups.

No cloud client, telemetry, credentials or implicit destination. A mounted external
store can be supplied by the operator later. Hash verification tests transport,
not AES-GCM authenticity: use backup-restore.ps1 for a real restoration drill.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil

NAME = re.compile(r"^(\d{8}T\d{6}Z)-[a-f0-9]{32}\.lhbk$")


def inspect_archive(path):
    path = Path(path)
    match = NAME.fullmatch(path.name)
    if not match or path.is_symlink() or not path.is_file():
        raise ValueError("Expected a regular LicenseHub encrypted backup, never a key or private configuration")
    created = datetime.strptime(match[1], "%Y%m%dT%H%M%SZ").replace(tzinfo=timezone.utc)
    with path.open("rb") as stream:
        if stream.read(8) != b"LHBK0001" or path.stat().st_size <= 36:
            raise ValueError("Invalid encrypted backup envelope")
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"name": path.name, "sha256": digest, "bytes": path.stat().st_size, "createdAt": created.isoformat()}


def copy_archive(source, destination):
    source = Path(source)
    info = inspect_archive(source)
    destination = Path(destination)
    if not destination.is_absolute() or not destination.is_dir() or destination.is_symlink():
        raise ValueError("Destination must be an existing, explicitly selected absolute directory")
    # Each archive owns a new directory. No existing backup can be overwritten.
    folder = destination / (source.stem + "-" + info["sha256"])
    try:
        folder.mkdir(mode=0o700)
    except FileExistsError:
        target = folder / source.name
        manifest = folder / 'manifest.json'
        if folder.is_symlink() or inspect_archive(target) != info or manifest.is_symlink() or not manifest.is_file() or json.loads(manifest.read_text(encoding='utf-8')) != info:
            raise ValueError("Existing copy differs or is incomplete; preserved for investigation") from None
        return {**info, "copied": False, "destination": str(target)}
    temporary = folder / "transfer.partial"
    with source.open("rb") as original, temporary.open("xb") as output:
        shutil.copyfileobj(original, output)
        output.flush()
        os.fsync(output.fileno())
    with temporary.open("rb") as stream:
        received_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    if received_hash != info["sha256"] or temporary.stat().st_size != info["bytes"]:
        raise ValueError("Copy verification failed; original and partial copy preserved")
    target = folder / source.name
    temporary.rename(target)
    (folder / "manifest.json").write_text(json.dumps(info, indent=2), encoding="utf-8")
    return {**info, "copied": True, "destination": str(target)}


def retention_plan(directory, keep_last=30, minimum_age_days=30, now=None):
    if keep_last < 2 or minimum_age_days < 1:
        raise ValueError("Retain at least two copies and one day; this command never deletes")
    now = now or datetime.now(timezone.utc)
    root = Path(directory)
    candidates = []
    # Only this tool's one-level copy folders. Never traverse arbitrary trees.
    for folder in root.iterdir():
        if folder.is_symlink() or not folder.is_dir():
            continue
        for path in folder.glob("*.lhbk"):
            info = inspect_archive(path)
            candidates.append({**info, "path": str(path)})
    candidates.sort(key=lambda item: (item["createdAt"], item["name"]), reverse=True)
    result = []
    for index, info in enumerate(candidates):
        age = (now - datetime.fromisoformat(info["createdAt"])).total_seconds() / 86400
        result.append({**info, "retain": index < keep_last or age < minimum_age_days})
    return {"mode": "plan-only", "deletions": 0, "keepLast": keep_last, "minimumAgeDays": minimum_age_days, "archives": result}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    copy = commands.add_parser("copy")
    copy.add_argument("source")
    copy.add_argument("destination")
    plan = commands.add_parser("retention-plan")
    plan.add_argument("directory")
    plan.add_argument("--keep-last", type=int, default=30)
    plan.add_argument("--minimum-age-days", type=int, default=30)
    args = parser.parse_args()
    try:
        result = copy_archive(args.source, args.destination) if args.command == "copy" else retention_plan(args.directory, args.keep_last, args.minimum_age_days)
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError):
        print("Backup operation failed; originals preserved. Review private paths and destination permissions.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
