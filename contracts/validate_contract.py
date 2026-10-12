"""Validación de diseño; no reemplaza pruebas HTTP contra API/PostgreSQL."""
import json
from datetime import datetime, timedelta
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import validate


def check_refs(value, document):
    if isinstance(value, dict):
        if "$ref" in value:
            ref = value["$ref"]
            if not ref.startswith("#/"):
                raise ValueError(f"Referencia externa no permitida: {ref}")
            target = document
            for part in ref[2:].split("/"):
                target = target[part.replace("~1", "/").replace("~0", "~")]
        for item in value.values():
            check_refs(item, document)
    elif isinstance(value, list):
        for item in value:
            check_refs(item, document)


def validate_example(document, name, schema_name):
    base = Path(__file__).parent
    instance = json.loads((base / "examples" / name).read_text(encoding="utf-8"))
    schema = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$ref": f"#/components/schemas/{schema_name}",
        "components": document["components"],
    }
    Draft202012Validator(schema, format_checker=FormatChecker()).validate(instance)
    if schema_name == "EntitlementSnapshot":
        active = instance["effectiveState"] == "Active"
        if instance["hasPremium"] != active:
            raise ValueError("hasPremium no coincide con estado")
        if not active and instance["features"]:
            raise ValueError("Derechos concedidos en estado bloqueado")
        evaluated = datetime.fromisoformat(instance["evaluatedAt"].replace("Z", "+00:00"))
        until = datetime.fromisoformat(instance["cacheUntil"].replace("Z", "+00:00"))
        if not evaluated <= until <= evaluated + timedelta(seconds=30):
            raise ValueError("Ventana de caché inválida")
        license_info = instance["license"]
        if license_info and license_info["isLifetime"] and license_info["expiresAt"] is not None:
            raise ValueError("Lifetime con vencimiento")
        if active and license_info and not license_info["isLifetime"]:
            expiry = datetime.fromisoformat(license_info["expiresAt"].replace("Z", "+00:00"))
            if until > expiry or evaluated >= expiry:
                raise ValueError("Derechos o caché después de expirar")


def main():
    document = yaml.safe_load((Path(__file__).parent / "openapi" / "licensehub-v1.yaml").read_text(encoding="utf-8"))
    validate(document)
    check_refs(document, document)
    operations = []
    for path, item in document["paths"].items():
        for method, operation in item.items():
            if method not in {"get", "post", "put", "patch", "delete"}:
                continue
            operations.append(operation["operationId"])
            if path.startswith("/api/v1/admin/"):
                requirements = operation["security"]
                if path == "/api/v1/admin/auth/csrf":
                    if method != "get" or requirements:
                        raise ValueError("CSRF seed debe ser GET anónimo")
                    continue
                requirement = requirements[0]
                auth_exception = path in {"/api/v1/admin/auth/login", "/api/v1/admin/auth/mfa"}
                if not auth_exception and "AdminSession" not in requirement:
                    raise ValueError("Operación administrativa sin sesión")
                if path == "/api/v1/admin/auth/mfa" and "MfaChallenge" not in requirement:
                    raise ValueError("MFA sin challenge autenticado")
                if method != "get" and "CsrfToken" not in requirement:
                    raise ValueError("Mutación administrativa sin CSRF")
    if len(operations) != len(set(operations)):
        raise ValueError("operationId duplicado")
    for example in ("active-two-minutes.json", "no-license.json", "lifetime.json"):
        validate_example(document, example, "EntitlementSnapshot")
    validate_example(document, "idempotency-conflict.json", "Problem")
    print("Contrato y ejemplos validados; pruebas de implementación aún pendientes.")


if __name__ == "__main__":
    main()
