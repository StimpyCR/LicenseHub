# LicenseHub

Estado: Fase 1, diseño previo a implementación. No hay API, SDK ni despliegue ejecutable todavía.

## Entregables

- `contracts/openapi/licensehub-v1.yaml`: contrato de integración v1.
- `contracts/compatibility.md`: reglas para el SDK independiente.
- `docs/architecture.md`: límites, seguridad y transacciones.
- `docs/data-model.md`: entidades, restricciones y estados.
- `docs/sdk-handoff.md`: firmas previstas, matriz de pruebas y rutas administrativas pendientes.
- `contracts/examples/`: fixtures para el consumidor.
- `contracts/validate_contract.py`: validación ejecutable de contrato y ejemplos.

La fase 2 solamente comienza después de validar el contrato y resolver errores críticos. LicenseHub.SDK vive en otro repositorio. Los ejemplos y el contrato no son evidencia de pruebas ejecutadas.

## Referencias oficiales

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 10 es LTS.
- [OpenAPI 3.1.1](https://spec.openapis.org/oas/v3.1.1.html): formato del contrato y semántica de esquemas.

## Estado de verificación

El 11 de octubre de 2026, después de reiniciar el entorno, se ejecutó `contracts/validate_contract.py` con salida 0: contrato OpenAPI, referencias y cuatro ejemplos validados. Se utilizó Python 3.12 con dependencias instaladas en `.venv`; las versiones están en `contracts/requirements-validation.txt`. No se encontraron archivos AGENTS.md en el espacio de trabajo. Esta entrega sigue siendo diseño draft: no hay API implementada ni pruebas HTTP, builds o pruebas de seguridad ejecutadas. La revisión semántica y las rutas administrativas adicionales siguen pendientes antes de congelar v1.
# LicenseHub
