"""Guard conservador del SDK v1: admite propiedades opcionales y operaciones nuevas.

No detecta cambios semánticos en descripciones: éstos requieren revisión humana.
El baseline inicial es de desarrollo, no una release publicada.
"""
from pathlib import Path
import yaml

IGNORED = {"description", "summary", "example", "examples", "tags", "externalDocs"}


def compare(old, new, location=""):
    if isinstance(old, dict):
        if not isinstance(new, dict):
            raise ValueError(f"Tipo incompatible: {location}")
        for key, value in old.items():
            if key in IGNORED:
                continue
            if key not in new:
                raise ValueError(f"Eliminación incompatible: {location}/{key}")
            compare(value, new[key], f"{location}/{key}")
        # Añadir una restricción a un esquema existente también puede romper contratos.
        for key in new.keys() - old.keys():
            if key not in IGNORED and key not in {"properties", "headers", "responses"}:
                raise ValueError(f"Restricción nueva requiere revisión/versionado: {location}/{key}")
    elif old != new:
        raise ValueError(f"Cambio incompatible: {location}")


def schema_refs(value):
    found = set()
    if isinstance(value, dict):
        reference = value.get("$ref", "")
        if reference.startswith("#/components/schemas/"):
            found.add(reference.rsplit("/", 1)[-1])
        for item in value.values():
            found |= schema_refs(item)
    elif isinstance(value, list):
        for item in value:
            found |= schema_refs(item)
    return found


def check_snapshot(baseline, current):
    required_schemas = set()
    for path, item in baseline["paths"].items():
        if path.startswith("/api/v1/licenses/"):
            compare(item, current["paths"].get(path), path)
            required_schemas |= schema_refs(item)
    # Parámetros, respuestas de error y autenticación también forman parte del SDK.
    for component in ("parameters", "responses", "securitySchemes"):
        for name, value in baseline["components"][component].items():
            compare(value, current["components"][component].get(name), f"components/{component}/{name}")
            required_schemas |= schema_refs(value)
    visited = set()
    while required_schemas - visited:
        name = sorted(required_schemas - visited)[0]
        old_schema = baseline["components"]["schemas"][name]
        new_schema = current["components"]["schemas"].get(name)
        # Propiedades nuevas opcionales son aditivas; los campos existentes permanecen intactos.
        if isinstance(new_schema, dict) and "properties" in old_schema and "properties" in new_schema:
            new_schema = {**new_schema, "properties": {key: new_schema["properties"].get(key) for key in old_schema["properties"]}}
        compare(old_schema, new_schema, f"schemas/{name}")
        required_schemas |= schema_refs(old_schema)
        visited.add(name)
    compare(baseline["security"], current["security"], "security")


def main():
    root = Path(__file__).parent
    current = yaml.safe_load((root / "openapi/licensehub-v1.yaml").read_text(encoding="utf-8"))
    snapshots = [root / "baseline/licensehub-v1.yaml"]
    frozen = root / "releases/1.0.0/licensehub-v1.yaml"
    if frozen.exists():
        import hashlib
        if hashlib.sha256(frozen.read_bytes()).hexdigest() != (frozen.parent / "SHA256").read_text().strip():
            raise ValueError("El contrato congelado no coincide con su checksum")
        snapshots.append(frozen)
    for snapshot in snapshots:
        check_snapshot(yaml.safe_load(snapshot.read_text(encoding="utf-8")), current)
    print(f"SDK v1 compatible con {len(snapshots)} snapshots; revisión semántica sigue siendo necesaria.")


if __name__ == "__main__":
    main()
