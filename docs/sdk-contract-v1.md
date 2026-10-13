# Contrato definitivo de integración v1

API 1.0.0, OpenAPI 3.1.1 y JSON Schema 2020-12. Fuente: contracts/openapi/licensehub-v1.yaml. Copia congelada: contracts/releases/1.0.0/licensehub-v1.yaml, con SHA256. LicenseHub.SDK conserva una copia idéntica y manifiesto en su repositorio independiente. No se publica NuGet desde LicenseHub.

| Operación | Entrada | Salida HTTP 200 | Scope |
|---|---|---|---|
| POST /api/v1/licenses/activate | licenseKey, subjectId; Idempotency-Key UUID no vacío | activationId, activatedAt, snapshot | licenses:activate |
| GET /api/v1/licenses/status | subjectId | EntitlementSnapshot | licenses:read |
| GET /api/v1/licenses/features | subjectId | El mismo snapshot completo | licenses:read |
| GET /api/v1/licenses/warnings | subjectId, cursor opcional, pageSize 1..100 | items y nextCursor nullable | warnings:read |

Bearer opaco de 32 bytes aleatorios, base64url sin padding (43 caracteres). No es JWT, clientId ni OAuth. Solo Authorization autentica. PostgreSQL resuelve aplicación/scopes en cada solicitud y rechaza credenciales expiradas/revocadas o aplicaciones inactivas. El UUID configurado en el SDK comprueba la respuesta; no selecciona la aplicación en el servidor. La credencial no habilita administración.

El backend consumidor autentica usuario y negocio antes de obtener subjectId. No acepta una elección arbitraria del navegador. Activación solo admite licenseKey/subjectId y respeta cliente preasignado. Varios dispositivos consultan el mismo negocio, sin volver a canjear la clave.

## DTO y estados

EntitlementSnapshot exige applicationId, subjectId, license, effectiveState, hasPremium, features, evaluatedAt, cacheUntil y entitlementVersion. license es null o {id, customerId, isLifetime, expiresAt}; todos esos campos son requeridos, incluido expiresAt nullable. UUID son cadenas, fechas RFC3339 UTC terminadas en Z y entitlementVersion es cadena decimal para preservar bigint.

Estados de licencia: Pending, Active, Suspended, Expired, Revoked y Cancelled. El snapshot añade None cuando no hay licencia canjeada. Una licencia emitida sin canjear es Pending administrativamente; integración devuelve None hasta activación. Solo Active concede Premium/funcionalidades; el resto devuelve false/lista vacía. Vitalicia declara isLifetime true y expiresAt null.

Warning exige id, code, severity, message, createdAt y licenseId. severity es Info, Warning o Error, con mayúsculas exactas. Códigos de advertencias y funcionalidades son strings extensibles. Advertencias y cursor se vinculan a aplicación, negocio y versión vigente de derechos.

HasFeatureAsync del SDK actual usa status porque contiene el mismo snapshot que features. También se verificó GET features directamente por HTTPS y JSON Schema; no se inventa otro método SDK.

## HTTP, idempotencia y caché

Errores application/problem+json requieren type, title, status, code y traceId; errors es opcional. 400: validación; 401: autenticación; 403: scope; 404: claves ajenas/inexistentes; 409: conflictos; 429: límite con Retry-After; 503: indisponibilidad; 500: error interno. El SDK conserva códigos desconocidos. Actualmente una clave ajena/inexistente devuelve not_found; no inferir dueño ni expiración de ese error.

Idempotency-Key conserva UUID y cuerpo al menos 30 días. Mismo UUID/cuerpo: resultado histórico original. Mismo UUID/otro cuerpo: 409 idempotency_conflict. Nuevo UUID para clave canjeada: 409 license_already_activated. PostgreSQL serializa concurrencia. El principal estable es la aplicación, para conservar idempotencia al rotar credenciales.

Un replay puede contener un snapshot Active anterior a revocación; no autoriza hoy. El SDK no lo cachea para autorización y consulta status. Renovaciones son administrativas, sin nueva clave, con ETag, operación permanente, permiso y motivo. Se verificó extensión única de un día y aumento de versión.

Respuestas no-store. Caché SDK desactivada por defecto; máximo 30 segundos, limitada por cacheUntil y expiresAt de una licencia activa finita. Nunca stale. Revocar no promete invalidación instantánea de cachés o solicitudes ya autenticadas. CancellationToken se propaga, pero cancelar HTTP no garantiza rollback.

## Cambios identificados

No cambiaron rutas, DTO, tipos, campos requeridos, enums, scopes ni códigos de éxito respecto al SDK revisado. OpenAPI actualiza descripciones; el baseline histórico se conserva y el guard también compara con la copia congelada.

Se endureció la implementación para cumplir additionalProperties: false de ActivationRequest: applicationId/customerId ajenos ahora producen 400. JSON duplicado también produce 400. El SDK conforme emite dos propiedades únicas y no necesita cambios. Consumidores que dependieran de tolerancia a entradas inválidas deben corregirse; este cambio observable está documentado, sin ocultarlo actualizando el baseline.

Cambiar estados cerrados, tipos, requisitos, seguridad o eliminar campos/operaciones exige API v2. Descripciones semánticas y caché requieren revisión explícita: comparar estructura no las certifica.
