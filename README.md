# LicenseHub

Estado: seguridad de fase 3 implementada con recuperación administrativa pendiente; fase 4 en curso. Backend con persistencia, migraciones, bootstrap privado, login/MFA/sesiones y gestión de aplicaciones/credenciales. Frontend Angular Material/PWA con acceso y formulario de aplicaciones. Los endpoints de licencias siguen pendientes. No hay SDK en este repositorio ni despliegue productivo.

## Entregables

- `contracts/openapi/licensehub-v1.yaml`: contrato de integración v1.
- `contracts/compatibility.md`: reglas para el SDK independiente.
- `docs/architecture.md`: límites, seguridad y transacciones.
- `docs/data-model.md`: entidades, restricciones y estados.
- `docs/sdk-handoff.md`: firmas previstas, matriz de pruebas y rutas administrativas pendientes.
- `contracts/examples/`: fixtures para el consumidor.
- `contracts/validate_contract.py`: validación ejecutable de contrato y ejemplos.

La fase 2 solamente comienza después de validar el contrato y resolver errores críticos. LicenseHub.SDK vive en otro repositorio. Los ejemplos y el contrato no son evidencia de pruebas ejecutadas.

## Ejecutar y continuar

Ver [instalación](docs/installation.md), [estado por fases](docs/progress.md), [seguridad](docs/security.md), [manual administrativo](docs/administration.md), [despliegue](docs/deployment.md) y [respaldo/restauración](docs/backup-restore.md).

La solución está en `backend/LicenseHub.slnx`; el frontend en `frontend/licensehub-web`. PostgreSQL requiere secretos de conexión suministrados por el operador. Compose no incluye contraseñas por defecto. El panel inicial comprueba disponibilidad, pero no simula licencias ni pagos.

## Referencias oficiales

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 10 es LTS.
- [OpenAPI 3.1.1](https://spec.openapis.org/oas/v3.1.1.html): formato del contrato y semántica de esquemas.
- [Angular compatibility](https://angular.dev/reference/versions): requisitos de Node/TypeScript/RxJS.
- [Npgsql EF Core 10](https://www.npgsql.org/efcore/release-notes/10.0.html): soporte del proveedor PostgreSQL.

## Estado de verificación

Verificado el 11 de octubre de 2026:

- Backend Release: compilación correcta, cero advertencias y errores.
- xUnit: 32 pruebas con evidencia de ejecución: 16 unitarias en Linux/Docker del incremento anterior (sin cambios), 8 de seguridad en Windows y 8 de integración con PostgreSQL real ejecutadas en este incremento. Incluyen MFA, CSRF, lockout, replay, concurrencia, expiración, cookie revocada, autenticación de integración y permisos emparejados por aplicación. Control de aplicaciones de Windows bloqueó la DLL unitaria; se verificó en Linux sin desactivar esa protección. BOLA de licencias y otros endpoints de negocio sigue pendiente.
- Angular: build de producción y lint TypeScript/Prettier correctos.
- OpenAPI, referencias y cuatro fixtures validados; compatibilidad con baseline de desarrollo comprobada.
- Migración y script SQL idempotente generados; EF no detectó cambios pendientes de modelo. Migración aplicada a PostgreSQL 17 real en Docker: 1 prueba de integración pasada, sin omisiones; comprobó claves foráneas entre aplicaciones y activación única.
- NuGet transitivo y npm sin vulnerabilidades reportadas al consultar sus bases de avisos.
- Revisión visual de escritorio claro y viewport móvil oscuro sin desbordamiento horizontal. Capturas en `docs/screenshots`.
- Imágenes API/web compiladas y Compose ejecutado con PostgreSQL real. HTTPS local: live/ready 200 Healthy; documento Angular, bundles, CSP y recursos PWA correctos. API y nginx ejecutan como usuarios no root. Instalación PWA en dispositivos pendiente.

Pendiente: workflow GitHub, generador C# del SDK, casos de uso administrativos/integración y pruebas productivas completas. Docker Desktop y su motor Linux están operativos. No se encontraron AGENTS.md en el espacio de trabajo. La validación Python usa versiones fijadas en `contracts/requirements-validation.txt`. El contrato sigue siendo draft, sin release publicada.




## Autenticación administrativa

[Inicialización de SuperAdmin](docs/admin-bootstrap.md) y [login/MFA/sesiones](docs/admin-authentication.md) implementados. Abrir https://localhost:8443 después del arranque local. El operador inicializó su cuenta mediante el flujo privado y confirmó el acceso con MFA.

Login devuelve un desafío, nunca una sesión sin MFA. PostgreSQL controla consumo único, replay de códigos y sesión de 30 minutos sin renovación; logout invalida cookies copiadas. Formularios reactivos, CSRF por solicitud, credenciales sin almacenamiento en navegador y permisos consultados del servidor. Capturas actualizadas en docs/screenshots/admin-login-light.jpg y admin-login-mobile-dark.jpg.

La [autenticación de integración](docs/integration-authentication.md) incorpora un esquema Bearer opaco con aplicación/scopes resueltos desde PostgreSQL y permisos administrativos emparejados por aplicación. Pendientes: aplicar las políticas a los futuros endpoints de negocio y recuperación administrativa. Gestión de credenciales en fase 4. No se expusieron módulos ficticios de licencias/pagos. OpenAPI mantiene la compatibilidad de integración SDK.

## Gestión de aplicaciones

Ver [operaciones, idempotencia y rotación](docs/application-management.md). Listado/creación/edición de aplicaciones disponibles en el panel. API de emisión/listado/revocación de credenciales implementada; su interfaz y catálogo Premium pendientes. Verificación de este incremento (12 de octubre de 2026): 11 pruebas de integración PostgreSQL y 8 de seguridad pasadas; contrato/compatibilidad, formato C#, lint/build Angular correctos. Las 16 pruebas unitarias previas no cambiaron ni se repitieron.

