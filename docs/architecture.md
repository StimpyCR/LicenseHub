# Arquitectura y decisiones — Fase 1

## Alcance y dependencias

Monolito modular: Domain no depende de frameworks; Application depende de Domain; Infrastructure implementa los puertos de Application; Api es la raíz de composición. Controladores adaptan HTTP a casos de uso y no contienen reglas de negocio. Angular consume exclusivamente la API administrativa. No se implementa el SDK aquí.

Backend objetivo: .NET 10 LTS, EF Core 10, PostgreSQL, Identity, Serilog, Hangfire persistente, xUnit. Fijar versiones exactas soportadas y archivos lock en fase 2, después de verificar compatibilidad real; no inventar números de versiones. OpenAPI 3.1.1 con JSON Schema 2020-12. Angular estable soportado, Material del mismo major, Signals para estado local y RxJS para flujos HTTP. PWA cachea recursos estáticos y nunca respuestas de autenticación, claves o derechos Premium.

Puertos: IClock (UTC/TimeProvider), ILicenseKeyHasher, IApplicationCredentialVerifier, IEmailSender, IAuditWriter, INotificationOutbox. Casos de uso: emitir, activar, consultar derechos, renovar, cambiar estado, confirmar pago, gestionar aplicación y credenciales. EF Core proporciona unidad de trabajo; no crear repositorios genéricos ni bus distribuido por anticipado.

## Identidad y límites de confianza

Para v1, integración con encabezado `Authorization: Bearer <credential>`: credencial opaca aleatoria de 256 bits, NO JWT. Contiene un identificador público y un secreto aleatorio; guardar solamente hash del secreto. Middleware resuelve ApplicationId desde una credencial activa y comprueba aplicación activa, expiración, revocación y scopes. Nunca aceptar ApplicationId del cuerpo de una integración como autoridad. Comparación de hash en tiempo constante; no registrar encabezados, claves ni secretos. TLS obligatorio.

Scopes mínimos: licenses:activate, licenses:read, warnings:read. StockMini autentica su usuario, obtiene su negocio en servidor y envía subjectId. LicenseHub confía en el backend de esa aplicación para afirmar ese sujeto, y NO autentica al usuario final de StockMini. Una credencial comprometida permite actuar dentro de esa aplicación: mitigar con scopes, rotación, rate limiting y auditoría. Nunca entregar esta credencial a navegador, móvil o instalable.

Administración: Identity con cookie de sesión HttpOnly, Secure, SameSite=Strict y antiforgery obligatorio en toda mutación, incluido login. Panel y API detrás del mismo origen; CORS deshabilitado por defecto. MFA requerido para administradores en producción, incluido SuperAdmin; no se conceden permisos administrativos hasta completar MFA. Registro público inexistente. Bootstrap de SuperAdmin mediante comando de operación, contraseña desde secret manager y enrolamiento MFA; bootstrap deshabilitado tras crear el primer administrador. Recuperación mediante códigos protegidos y procedimiento auditado.

Roles: SuperAdmin gestiona asignaciones y aplicaciones; ApplicationAdmin y Operator están limitados por ApplicationUserGrants. Capacidades separadas para emisión, renovación, pagos, revocación y lectura de auditoría. Toda consulta administrativa comprueba el permiso sobre la aplicación del objeto. Un UUID no es autorización. Recursos ajenos devuelven 404. SuperAdmin es el único rol con acceso global explícito.

No hay JWT en la autenticación v1. Si una futura versión introduce JWT, bloquear su activación hasta implementar validación de firma, issuer, audience, lifetime, algoritmo permitido fijo y claves confiables con rotación; nunca seleccionar algoritmos del token sin allowlist.

## Activación, concurrencia e idempotencia

Clave: 32 bytes de CSPRNG codificados base64url con prefijo LH1_; comparar SHA-256 del código ASCII exacto. Con esta entropía no es una contraseña humana. Nunca persistir texto claro de claves, tampoco en auditoría o respuesta idempotente. Emisión devuelve cada clave una sola vez con Cache-Control:no-store. No se normalizan mayúsculas/minúsculas. Un fallo de transporte después del commit de emisión requiere revocar las claves inaccesibles y emitir nuevas: el replay devuelve metadatos sin secretos y `codesRecoverable:false`.

Activación: buscar hash y ApplicationId; transacción con bloqueo de fila de licencia. Comprobar estados y expiración dentro del bloqueo. Primera activación vincula sujeto y crea LicenseActivation; restricción única LicenseActivation.LicenseId impide segundo canje. Para inicio al activar se fija ActivatedAt, StartsAt y ExpiresAt en ese commit. Otra clave idempotente para un código ya canjeado devuelve 409, aun si se presenta el mismo sujeto. Múltiples dispositivos consultan la licencia vinculada; no vuelven a canjear.

IdempotencyRecord está separado por ApplicationId + principal estable + operation + Idempotency-Key. Cambio de credencial no cambia el principal de aplicación. Hash canónico de método/ruta/cuerpo incluye subjectId. Clave repetida con distinto contenido:409; operación en curso:409 con Retry-After. Guardar resultado y mutación atómicamente en PostgreSQL. Replay de activación devuelve resultado original aunque luego se revoque; NO usar esa respuesta como autorización actual. Siempre consultar status/features. Retención mínima 30 días; unicidad de activación y PaymentRenewal previene duplicados después de eliminar el registro. Las renovaciones manuales exigen operationId permanente único por aplicación.

## Derechos, expiración y caché

Snapshot: estado efectivo, licencia seleccionada, features, evaluatedAt, expiresAt, cacheUntil, entitlementVersion. Una licencia activa por (ApplicationId,SubjectId) como política v1. Antes de emitir/vincular otra, expirar la anterior bajo bloqueo de la fila Customer y comprobar unicidad. Suspended conserva el vínculo y bloquea reemplazos accidentales hasta decisión administrativa.

Consultas calculan expiración al leer: ExpiresAt <= now implica Expired y cero derechos aunque el job aún no haya actualizado State. Suspended, Revoked, Cancelled, Pending o ausencia de licencia: cero derechos. No licencia:200 con license:null y effectiveState:None. `None` pertenece al snapshot, no al estado persistido.

SDK puede cachear como máximo 30 segundos y nunca después de expiresAt o cacheUntil. Suspensión/revocación puede tardar hasta ese límite en consumidores correctamente implementados, más tolerancia de reloj documentada de hasta 5 segundos. Operaciones sensibles requieren consulta sin caché. Cache-Control:no-store evita caché HTTP compartida; la caché explícita del SDK usa (ApplicationId,SubjectId). Errores de red no autorizan Premium al vencer cacheUntil. Aplicación desactivada o credencial revocada rechaza solicitudes inmediatamente en servidor, sin eliminar snapshots ya entregados. StockMini verifica permisos de usuario y negocio además de derechos Premium en cada endpoint protegido.

## Renovación, pagos y eventos

Renovación transaccional: bloqueo licencia, operationId único, registro inmutable de vencimiento anterior/nuevo, administrador y motivo. Base temporal max(now,ExpiresAt) para Active/Expired. Duration/Until/Lifetime son alternativas exclusivas. Fecha específica debe superar ahora y el vencimiento actual si existe. Renovar Suspended o Revoked requiere reactivate:true y permiso licenses:reactivate; Cancelled es terminal. Pending no se renueva. Reactivación explícita conserva sujeto. Licencia vitalicia no admite extensión finita; sí suspensión/revocación.

Pago manual Pending -> Confirmed/Rejected; Confirmed -> Refunded; terminales no vuelven atrás. Confirmación requiere permiso payments:confirm. Pago con intención de renovación ejecuta confirmación + renovación + auditoría + outbox en una transacción. Si la renovación falla, no se confirma. Restricción única LicenseRenewal.PaymentId. Reembolso registra evento y no revoca derechos automáticamente: decisión administrativa aparte. No almacenar datos de tarjetas. Gateways futuras se adaptan a un puerto; no implementar cobro ficticio.

Outbox PostgreSQL y Hangfire PostgreSQL: publicar trabajos después del commit y reconciliar pendientes. Jobs con lease, reintentos exponenciales y máximo configurable, estados Failed inspeccionables. Deduplicar por LicenseId + entitlementVersion + eventType + scheduleOffset + channel. SMTP no garantiza exactamente una entrega: posible duplicado si el proveedor acepta y el proceso cae antes de registrar éxito; usar clave idempotente del proveedor cuando exista y documentar al menos una vez. Alertas 7/3/1 días solo si el instante queda después de emisión/activación; corta duración usa offsets configurados menores a la vigencia y siempre aviso de expiración. Renovar cancela avisos obsoletos mediante entitlementVersion. Plantillas tienen variables permitidas y HTML sanitizado. GET warnings lee avisos de la aplicación/sujeto, no marca emails como enviados.

## Operación y seguridad

Problem Details centralizado sin stack traces; correlationId seguro. Validación server-side de tamaños, enumeraciones y combinaciones. EF parametrizado; no SQL interpolado. Angular escapa texto y no usa bypassSecurityTrustHtml para contenido administrativo. CSP, HSTS en producción y confianza en forwarded headers limitada al proxy configurado. Rate limits por aplicación/credencial, principal e IP; activación más estricta. Identity lockout, respuestas genéricas y MFA mitigan fuerza bruta.

Auditoría append-only con usuario/principal, aplicación, acción, objeto, motivo y correlationId; redactar PII sensible y secretos. Cuenta SQL del runtime sin DDL; migraciones con identidad separada. Backup cifrado con claves en secret manager, retención definida por operación y restauración periódica en entorno aislado. No afirmar restauración probada hasta ejecutarla. Hangfire dashboard cerrado por defecto o restringido a SuperAdmin con MFA.

CI de fase 2: restore locked, build, xUnit con PostgreSQL real/Testcontainers, build Angular, lint, validación OpenAPI y ejemplos, comparación semántica con contrato publicado, escaneo NuGet/npm y análisis estático. No despliegue automático de producción. Antes de publicación: pruebas concurrentes, BOLA, cookies/CSRF/MFA, credenciales revocadas, expiración de dos minutos y restauración.

## Puerta de salida de Fase 1

Contrato revisado, parser OpenAPI 3.1 ejecutado, ejemplos validados y decisiones aceptadas; no iniciar implementación con errores críticos. El 11 de octubre de 2026 el validador local terminó con salida 0 para estructura OpenAPI, referencias y cuatro ejemplos. Sigue pendiente revisión semántica y validación con el generador C# elegido. El SDK puede comenzar diseño de modelos y pruebas desde el contrato, pero no publicar un paquete como compatible antes de esta puerta.
