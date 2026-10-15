"""Read-only local release gate; never uploads reports or approves deployment."""
import argparse
from datetime import datetime, timedelta, timezone
import json
import hashlib
import os
from pathlib import Path
import re
import shutil
import subprocess


class ScanRejected(ValueError):
    pass


def timestamp(value):
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            raise ValueError()
        return parsed
    except (AttributeError, TypeError, ValueError):
        raise ScanRejected("Fecha de evidencia ausente o invalida.") from None


SCANNER = "aquasec/trivy:0.75.0@sha256:af6acf9a6b85dfe389a1941505c0ce9efef52a4719635e1a962f022a3d855daa"


def validate_scan(report, database, image_id, now, evidence, report_hash, max_age_hours=48):
    if not all(isinstance(value, dict) for value in (report, database, evidence)):
        raise ScanRejected("Estructura de evidencia invalida.")
    if not 1 <= max_age_hours <= 168:
        raise ScanRejected("La antiguedad maxima debe estar entre 1 y 168 horas.")
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", image_id or ""):
        raise ScanRejected("Identidad local de imagen invalida.")
    metadata = report.get("Metadata") or {}
    if not isinstance(metadata, dict):
        raise ScanRejected("Metadatos de imagen invalidos.")
    if evidence.get("schemaVersion") != 1 or evidence.get("imageId") != image_id:
        raise ScanRejected("El informe no corresponde a la imagen local actual.")
    if not isinstance(report_hash, str) or evidence.get("reportSha256") != report_hash or not re.fullmatch(r"[0-9a-f]{64}", report_hash):
        raise ScanRejected("El informe cambio desde el escaneo; repetir analisis.")
    if evidence.get("network") != "none" or evidence.get("scanner") != SCANNER:
        raise ScanRejected("Falta evidencia del procedimiento local esperado.")
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", metadata.get("ImageID") or ""):
        raise ScanRejected("Falta identidad de configuracion en el informe.")
    created = timestamp(report.get("CreatedAt"))
    updated = timestamp(database.get("UpdatedAt"))
    completed = timestamp(evidence.get("scannedAt"))
    if timestamp(evidence.get("databaseUpdatedAt")) != updated:
        raise ScanRejected("Avisos distintos a los utilizados; repetir escaneo.")
    limit = timedelta(hours=max_age_hours)
    if any(date > now + timedelta(minutes=5) or now - date > limit for date in (created, updated, completed)):
        raise ScanRejected("Informe o base de avisos caducados; repetir escaneo local.")
    if updated > created or created > completed:
        raise ScanRejected("La base de avisos cambio despues del informe; repetir escaneo.")
    operating_system = metadata.get("OS")
    if not isinstance(operating_system, dict) or not operating_system.get("Family") or operating_system.get("EOSL", False):
        raise ScanRejected("Sistema operativo no identificado o fuera de soporte.")
    results = report.get("Results")
    if not isinstance(results, list) or not all(isinstance(item, dict) for item in results) or not any(
        item.get("Class") == "os-pkgs" and isinstance(item.get("Packages"), list)
        and any(isinstance(package, dict) and package.get("Name") for package in item["Packages"]) for item in results
    ):
        raise ScanRejected("Falta evidencia de paquetes del sistema; no equivale a cero hallazgos.")
    counts = dict.fromkeys(("CRITICAL", "HIGH", "MEDIUM", "LOW"), 0)
    unknown = False
    for item in results:
        vulnerabilities = item.get("Vulnerabilities")
        if vulnerabilities is None:
            vulnerabilities = []
        if not isinstance(vulnerabilities, list) or not all(isinstance(value, dict) for value in vulnerabilities):
            raise ScanRejected("Lista de hallazgos invalida.")
        for vulnerability in vulnerabilities:
            severity = vulnerability.get("Severity")
            if severity not in counts:
                unknown = True
                continue
            counts[severity] += 1
    if counts["CRITICAL"] or counts["HIGH"]:
        raise ScanRejected(f"No apta: {counts['CRITICAL']} criticas y {counts['HIGH']} altas pendientes.")
    if unknown:
        raise ScanRejected("Severidad desconocida; requiere revision manual.")
    return counts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", help="Existing local image; no pulls or registry calls")
    parser.add_argument("--max-age-hours", type=int, default=48)
    args = parser.parse_args()
    try:
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._/:@-]{0,511}", args.image):
            raise ScanRejected("Referencia de imagen invalida.")
        docker = shutil.which("docker") or str(Path(os.environ.get("LOCALAPPDATA", "")) / "Programs/DockerDesktop/resources/bin/docker.exe")
        image_id = subprocess.run(
            [docker, "image", "inspect", args.image, "--format", "{{.Id}}"],
            check=True, capture_output=True, text=True, timeout=30,
        ).stdout.strip()
        directory = Path(__file__).resolve().parents[1] / ".tooling/security"
        filename = re.sub(r"[^A-Za-z0-9._-]", "-", args.image) + ".json"
        raw = (directory / filename).read_bytes()
        report = json.loads(raw.decode("utf-8-sig"))
        evidence_path = directory / (filename.removesuffix(".json") + ".evidence.json")
        evidence = json.loads(evidence_path.read_text(encoding="utf-8-sig"))
        database = json.loads((directory / "trivy-db/db/metadata.json").read_text(encoding="utf-8-sig"))
        counts = validate_scan(report, database, image_id, datetime.now(timezone.utc), evidence, hashlib.sha256(raw).hexdigest(), args.max_age_hours)
        print(f"Sin criticas/altas en este escaneo. Medias={counts['MEDIUM']}, bajas={counts['LOW']}.")
        print("No autoriza despliegue ni acredita compatibilidad, restauracion o ausencia de vulnerabilidades desconocidas.")
        return 0
    except ScanRejected as error:
        print(str(error))
        return 1
    except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
        print("Evidencia local no disponible o invalida; no se aprueba la imagen.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
