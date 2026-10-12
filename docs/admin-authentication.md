# Acceso administrativo con MFA

Implementados: `GET /api/v1/admin/auth/csrf`, `POST /login`, `POST /mfa`, `GET /session` y `POST /logout` bajo el mismo prefijo. El panel Angular los consume desde HTTPS. No existen registro público, enrolamiento HTTP ni recuperación pública. Inicializar la cuenta con [bootstrap privado](admin-bootstrap.md).

## Flujo y límites

1. Obtener CSRF: cookie semilla HttpOnly y token en respuesta JSON, sin caché.
2. Enviar email/contraseña con `X-CSRF-TOKEN`. Datos incorrectos, cuenta bloqueada o MFA ausente devuelven el mismo error `401 authentication_required`. Un email desconocido realiza trabajo de hash sin credencial predeterminada válida. No se restablece el contador de fallos por acertar solo la contraseña.
3. Un login correcto devuelve 202 y challenge Secure/HttpOnly/Strict de cinco minutos, sin cookie administrativa. MFA es obligatorio también en Development.
4. Enviar código TOTP con challenge y CSRF. El proveedor oficial Identity admite hasta dos pasos de 30 segundos a cada lado; sincronizar relojes. Se serializa la operación por usuario en PostgreSQL, se consume el challenge una sola vez y se registra hash del código por tres minutos. Dos challenges no permiten reutilizar un código durante esa ventana. No registrar códigos ni claves.
5. Tras MFA se emite una cookie Secure/HttpOnly/Strict y sesión persistente con duración máxima de 30 minutos, sin renovación deslizante. Obtener nuevo CSRF, ahora ligado a la identidad autenticada, antes de logout u otras mutaciones.
6. Logout revoca la sesión en PostgreSQL y elimina cookies. Una copia de la cookie anterior ya no autoriza. Se validan sesión, SecurityStamp, MFA, lockout y permisos actuales en cada solicitud. Una base no disponible impide validar acceso.

Límites: login 10 solicitudes/minuto/IP, MFA 20/minuto/IP y límite global 120/minuto/IP. Identity bloquea cinco fallos de contraseña/MFA durante 15 minutos. Los contadores se actualizan bajo bloqueo por usuario; un código repetido también cuenta como fallo. JSON rechaza propiedades no definidas; cuerpo de auth limitado a 8 KiB. CSRF faltante/incorrecto devuelve 403. Errores usan Problem Details sin credenciales.

Los endpoints adaptan transporte, cookies y antiforgery; Application define el caso de uso e Infrastructure implementa Identity, transacciones, auditoría y persistencia. No hay lógica de autorización de licencias en estos endpoints.

## Permisos y operación

`Session.capabilities` y `applicationIds` son una vista para UI. SuperAdmin se identifica por capability `superadmin`. Las capabilities agregadas no representan un producto cartesiano de permisos/aplicaciones: futuros endpoints deben comprobar cada fila `(UserId, ApplicationId, Capability)` en el servidor. La gestión de grants y la autenticación de integraciones siguen pendientes; no hay endpoints administrativos de negocio habilitados.

La migración `AdminAuthentication` añade AdminAuthChallenges, AdminMfaUses y AdminSessions con FK restrictiva a Identity e índices de expiración. Auditoría conserva eventos login/challenge/MFA/logout. Los registros expirados pueden limpiarse en tareas de mantenimiento futuras; no confieren acceso después de su fecha aunque sigan almacenados. Hasta implementar esa limpieza persistente, monitorizar crecimiento y aplicar una política operativa revisada. No eliminar usos MFA aún vigentes.

La clave TOTP permanece en el almacén estándar Identity, protegido por privilegios/cifrado de almacenamiento y backups. DataProtection protege cookies, no cifra automáticamente ese campo. Recuperación y rotación operativa de SuperAdmin disponibles mediante [CLI privada](admin-recovery.md); administración de otros usuarios y recuperación autoservicio pendientes. Ver [limpieza y programación](auth-maintenance.md). El operador inicializó su cuenta mediante el flujo privado y confirmó el acceso; no se publican credenciales.

## Verificación ejecutada

Pruebas HTTP con PostgreSQL real: CSRF obligatorio, challenge sin sesión, rechazo de código mal formado, MFA correcto, cookie segura, cambio de token CSRF al autenticar y rechazo de cookie copiada tras logout. Pruebas de servicio: dos consumos MFA concurrentes producen una sola sesión; código repetido, challenge expirado, sesión expirada y userId incorrecto se rechazan; cambio de SecurityStamp y lockout invalidan acceso. Usuarios y bases son efímeros y separados del entorno local del operador.

El panel guarda en memoria el token CSRF y la vista de sesión; no guarda contraseñas ni códigos en localStorage/sessionStorage. `licensehub-theme` es la única preferencia local. Las autorizaciones reales pertenecen al backend y no al estado visual del panel.
