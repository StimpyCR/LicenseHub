import json
import sys
from pathlib import Path

report = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
vulnerabilities = []
for project in report.get("projects", []):
    for framework in project.get("frameworks", []):
        for kind in ("topLevelPackages", "transitivePackages"):
            for package in framework.get(kind, []):
                if package.get("vulnerabilities"):
                    vulnerabilities.append(package["id"])
if report.get("problems"):
    raise SystemExit("No fue posible completar el análisis NuGet.")
if vulnerabilities:
    raise SystemExit("Dependencias vulnerables: " + ", ".join(sorted(set(vulnerabilities))))
print("NuGet: sin vulnerabilidades reportadas.")
