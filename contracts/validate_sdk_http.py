"""Validate secret-free, real HTTPS responses consumed during SDK compatibility."""
import json
import sys
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator, FormatChecker

root = Path(__file__).parent
document = yaml.safe_load((root / "openapi/licensehub-v1.yaml").read_text(encoding="utf-8"))
directory = Path(sys.argv[1])
count = 0
operations = set()
for stage in ("before", "after"):
    result = json.loads((directory / f"{stage}-result.json").read_text(encoding="utf-8"))
    if result["passed"] is not True:
        raise ValueError("SDK compatibility stage did not pass")
    for item in json.loads((directory / f"{stage}-wire.json").read_text(encoding="utf-8")):
        if "request" in item or "headers" in item:
            raise ValueError("Real wire evidence must exclude secret requests and headers")
        operation = document["paths"][item["path"]][item["method"]]
        response = operation["responses"][str(item["status"])]
        if "$ref" in response:
            response = document["components"]["responses"][response["$ref"].rsplit("/", 1)[-1]]
        schema = response["content"][item["media"]]["schema"]
        Draft202012Validator({**schema, "components": document["components"]}, format_checker=FormatChecker()).validate(item["response"])
        operations.add((item["path"], item["method"]))
        count += 1
required = {(f"/api/v1/licenses/{name}", "post" if name == "activate" else "get") for name in ("activate", "status", "features", "warnings")}
if not required <= operations:
    raise ValueError("All four integration operations must be exercised")
print(f"{count} respuestas HTTPS reales del SDK compatibles; cuatro operaciones verificadas.")
