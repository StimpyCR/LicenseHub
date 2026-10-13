# LicenseHub

Estado: fases 1–9 implementadas y compatibilidad SDK v1 verificada. Fase 10 en curso: backup cifrado/restauración real, revisión de seguridad y preparación operativa. No se desplegó a producción. El SDK permanece en su propio repositorio.

## Entregables

- `contracts/openapi/licensehub-v1.yaml`: contrato de integración v1.
- `contracts/compatibility.md`: reglas para el SDK independiente.
- `docs/architecture.md`: límites, seguridad y transacciones.
- `docs/data-model.md`: entidades, restricciones y estados.
- `docs/sdk-handoff.md`: contrato cerrado y matriz de integración del SDK independiente.
- `contracts/examples/`: fixtures para el consumidor.
- `contracts/validate_contract.py`: validación ejecutable de contrato y ejemplos.

LicenseHub.SDK vive en otro repositorio. El contrato v1 está congelado; ver docs/sdk-contract-v1.md y docs/sdk-live-integration.md para evidencia real.

## Ejecutar y continuar

Ver [instalación](docs/installation.md), [estado por fases](docs/progress.md), [seguridad](docs/security.md), [manual administrativo](docs/administration.md), [despliegue](docs/deployment.md) y [respaldo/restauración](docs/backup-restore.md).

La solución está en `backend/LicenseHub.slnx`; el frontend en `frontend/licensehub-web`. PostgreSQL requiere secretos de conexión suministrados por el operador. Compose no incluye contraseñas por defecto. El panel administra aplicaciones, clientes, licencias, renovaciones, pagos, notificaciones y auditoría con persistencia real.

## Referencias oficiales

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 10 es LTS.
- [OpenAPI 3.1.1](https://spec.openapis.org/oas/v3.1.1.html): formato del contrato y semántica de esquemas.
- [Angular compatibility](https://angular.dev/reference/versions): requisitos de Node/TypeScript/RxJS.
- [Npgsql EF Core 10](https://www.npgsql.org/efcore/release-notes/10.0.html): soporte del proveedor PostgreSQL.

## Estado de verificación

Fase 9: API 74 pruebas y SDK 94 aprobadas sin omisiones; HTTPS real y contrato congelado, CI aprobado. La evidencia actual de fase 10 y sus pendientes están en [fase10-verification.md](docs/fase10-verification.md). [progress.md](docs/progress.md) conserva el historial de incrementos. Los ejemplos por sí solos no acreditan compatibilidad.

## Autenticación administrativa

[Inicialización de SuperAdmin](docs/admin-bootstrap.md) y [login/MFA/sesiones](docs/admin-authentication.md) implementados. Abrir https://localhost:8443 después del arranque local. El operador inicializó su cuenta mediante el flujo privado y confirmó el acceso con MFA.

Login devuelve un desafío, nunca una sesión sin MFA. PostgreSQL controla consumo único, replay de códigos y sesión de 30 minutos sin renovación; logout invalida cookies copiadas. Formularios reactivos, CSRF por solicitud, credenciales sin almacenamiento en navegador y permisos consultados del servidor. Capturas actualizadas en docs/screenshots/admin-login-light.jpg y admin-login-mobile-dark.jpg.

La [autenticación de integración](docs/integration-authentication.md) incorpora un esquema Bearer opaco con aplicación/scopes resueltos desde PostgreSQL y permisos administrativos emparejados por aplicación. Las políticas se aplican a endpoints de negocio; gestión de credenciales y recuperación privada implementadas. OpenAPI mantiene la compatibilidad de integración SDK.

## Gestión de aplicaciones

Ver [operaciones, idempotencia y rotación](docs/application-management.md). Listado/creación/edición de aplicaciones, interfaz de emisión/listado/revocación de credenciales y catálogo Premium disponibles. Recuperación y rotación operativa de SuperAdmin mediante [CLI privada](docs/admin-recovery.md); [limpieza de autenticación](docs/auth-maintenance.md) implementada, tarea horaria persistente habilitada y verificada en Hangfire local. Verificación del incremento (12 de octubre de 2026): 15 pruebas de integración PostgreSQL y 8 de seguridad pasadas; contrato/compatibilidad, formato C#, lint/build Angular correctos. Build Angular avisa 502.20 kB frente al presupuesto inicial de 500 kB, sin error. Las 16 pruebas unitarias previas no cambiaron ni se repitieron.


## Administradores delegados

Ver [gestión y enrolamiento privado](docs/admin-users.md). Creación pendiente, permisos por aplicación y suspensión disponibles para SuperAdmin. Recuperación privada conserva roles/permisos y rechaza cuentas suspendidas. Verificadas 18 pruebas de integración y 9 de seguridad en este incremento, además del contrato y revisión visual con datos aislados. Clientes y licencias implementados posteriormente; ver los módulos siguientes.

## Clientes y licencias

[Operaciones v1](docs/license-operations.md): clientes, emisión individual/lotes y activación transaccional disponibles. Integraciones pueden consultar estado y funcionalidades por subjectId de su propio backend. Reintentos de emisión no recuperan códigos y reintentos de activación son históricos. Panel conectado y 51 pruebas Docker pasadas; contrato HTTP comprobado. LicenseHub.SDK continúa en repositorio independiente.

## Renovaciones e historial

[Renovación y cambios de estado](docs/license-lifecycle.md) disponibles con permisos, motivo, concurrencia e idempotencia. Historial protegido por PostgreSQL. 58 pruebas pasadas y ocho respuestas HTTP contrastadas con OpenAPI. La caché del consumidor conserva una ventana máxima de 30 segundos; no se promete revocación instantánea.

## Pagos y notificaciones

[Pagos manuales y notificaciones](docs/payments-notifications.md) disponibles. Confirmación y renovación atómicas, outbox persistente, alertas y plantillas por aplicación, transporte SMTP y avisos de integración. 67 pruebas pasadas y trece respuestas HTTP validadas. El proveedor SMTP requiere configuración privada; no se simulan envíos.

## Informes administrativos

[Dashboard, auditoría y CSV](docs/admin-reports.md) por aplicación, con permisos delegables y búsqueda de clientes. Exportación sin claves, neutralización de fórmulas y límite explícito de 10 000 filas. 72 pruebas pasadas y quince respuestas HTTP validadas. La PWA avisa de nuevas versiones y permite recargar voluntariamente después de guardar los códigos recién emitidos.

## Compatibilidad SDK verificada

[Contrato definitivo v1](docs/sdk-contract-v1.md) y [pruebas HTTPS desde host](docs/sdk-live-integration.md). Contrato congelado con checksum en contracts/releases/1.0.0. SDK existente separado: 94 pruebas pasadas sin omisiones; LicenseHub 74 pasadas. Aplicaciones, clientes y licencias de ensayo persistentes con credenciales privadas; no despliegue productivo.


## Preparación operativa

[Fase 10 en curso](docs/fase10-verification.md): respaldo cifrado y restauración real aislada, pruebas de integridad/permisos, guards productivos y plantilla de despliegue separada. Gmail configurable, sin envíos reales. Persisten pendientes de cuenta, infraestructura, custodia externa y escaneo de imágenes autorizado.
