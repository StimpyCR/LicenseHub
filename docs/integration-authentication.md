# Autenticación de aplicaciones

La fase 3 incorpora el esquema `IntegrationCredential` y las políticas `licenses:activate`, `licenses:read` y `warnings:read`. La gestión de credenciales ya está implementada según application-management.md; los cuatro endpoints de licencias están implementados y verificados con LicenseHub.SDK. Ver sdk-contract-v1.md y sdk-live-integration.md.

El contrato OpenAPI establece un secreto opaco de 32 bytes aleatorios, base64url sin padding (43 caracteres). La emisión muestra el secreto una vez y guarda SHA-256 hexadecimal en mayúsculas. Un índice único permite resolver la credencial sin duplicidades. No se utiliza JWT ni se comparte el secreto con Angular, aplicaciones móviles o el navegador de StockMini.

El backend consumidor envía `Authorization: Bearer <secret>` por HTTPS. El esquema consulta PostgreSQL en cada solicitud, exige aplicación activa, credencial sin revocar y `expiresAt > now` UTC; rechaza scopes desconocidos. Solo se acepta el encabezado: query string y cookies no autentican una integración. La aplicación y el identificador de credencial provienen de la base, sin confiar en `applicationId` del consumidor. Los valores de credenciales no se registran.

Una credencial inválida produce 401 con `authentication_required` y `WWW-Authenticate: Bearer`. La política de un endpoint exige su scope; si falta, responde 403 con `forbidden`. Los errores mantienen ProblemDetails y traceId. El esquema de integración se selecciona explícitamente por política: una sesión administrativa no sustituye una credencial de integración y una credencial de integración no habilita la sesión administrativa.

No hay caché de autenticación en LicenseHub: después de confirmar una revocación o desactivación, una nueva comprobación consulta el estado actualizado. Una solicitud que ya se autenticó puede continuar; las operaciones transaccionales sensibles deberán volver a comprobar el estado bajo el bloqueo correspondiente. La caché de derechos Premium del SDK sigue la política independiente de hasta 30 segundos, limitada por expiración; esto no promete revocación instantánea en el consumidor.

Los servicios reciben CancellationToken y el handler transmite RequestAborted. El límite global por IP se aplica antes de autenticar; el rate limiting distribuido o por aplicación requiere trabajo adicional si se despliegan varias réplicas.

## Autorización administrativa por aplicación

`IApplicationAccess` consulta el permiso exacto `(userId, applicationId, capability)`, exige MFA habilitado y ausencia de lockout, y reconoce SuperAdmin. `ApplicationCapabilityHandler` requiere una identidad administrativa con MFA verificado. Los endpoints administrativos seleccionan exclusivamente el esquema de sesión y comprueban permisos sobre el ApplicationId real del recurso obtenido de la base. Un rechazo sobre un recurso ajeno se traducirá a 404 antes de devolver datos. Nunca usar un applicationId ajeno al recurso ni combinar listas agregadas de permisos y aplicaciones.

La validación de sesión persistente sigue en AdminSessionCookieEvents. La lista de capabilities de `/admin/auth/session` sirve para presentación y no otorga un producto cartesiano de permisos entre aplicaciones.

## Verificación

Pruebas de integración sobre bases PostgreSQL aisladas comprueban credenciales desconocidas, expiradas, revocadas, aplicación inactiva, scopes inválidos, cancelación, resolución de aplicación desde secreto, rechazo de secretos en URL, separación de esquemas y permisos emparejados por aplicación. Se complementan con pruebas BOLA de licencias y compatibilidad HTTPS real del SDK.
