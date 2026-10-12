# Seguridad: base implementada y trabajo pendiente

Implementado en fase 2: arranque sin conexión hardcodeada; Identity con contraseña mínima de 14 caracteres, lockout y email único; cookies Secure/HttpOnly/Strict; política administrativa cerrada que requiere sesión y claim MFA; servicios antiforgery; HTTP rechazado para rutas fuera de sondas; proxies explícitos; no CORS permisivo; rate limiting por IP; Problem Details; no-store; CSP/nosniff; Serilog sin registro de cuerpos ni encabezados; auditoría append-only en contexto y trigger SQL; FK por aplicación y control optimista Version.

Login/MFA, CSRF, sesión persistente y logout están implementados; ver [guía](admin-authentication.md). Todavía no existen enrolamiento HTTP, endpoints de negocio con permisos por aplicación ni credenciales de integración en ejecución. El bootstrap offline está implementado y exige confirmar TOTP antes de crear SuperAdmin; ver [guía](admin-bootstrap.md). No hay endpoints de negocio expuestos. Añadir éstos exige pruebas de autorización/CSRF/BOLA, rate limiting por aplicación y validadores de comandos antes de habilitar el panel.

Producción requiere DataProtection__KeyPath persistente y DataProtection__CertificatePath a un PFX con clave privada vigente. CertificatePassword proviene del secret manager, nunca del repositorio. Archivos y certificado se montan con permisos mínimos. Certificado de cifrado de claves de sesión es independiente de TLS. Rotar conservando certificados antiguos necesarios para descifrar claves pendientes y ensayar recuperación. En Development se permite almacenamiento local sin cifrado de sesión; no usar esa configuración en producción.

La cuenta del runtime no debe poder cambiar esquema, deshabilitar triggers o editar auditoría; administrador de base y dueño del esquema pueden hacerlo y deben quedar bajo control operativo externo. El trigger no evita TRUNCATE privilegiado ni manipulación por superusuario: excluir esos privilegios del runtime. Auditoría externa o almacenamiento inmutable protege frente a administradores de base maliciosos.

No activar SensitiveDataLogging de EF ni volcar configuración o secretos en errores. Registrar solo IDs, acción, principal y correlationId. Aplicar redacción antes de persistir errores de SMTP/proveedores en NotificationLogs. Plantillas HTML no habilitadas en contrato inicial: texto plano con variables permitidas, evitando ejecución arbitraria.

El runtime .NET instalado localmente es 10.0.11; los paquetes Microsoft seleccionados son 10.0.12. Compilación local no certifica parches del host de producción: instalar runtime soportado con parches vigentes y comprobar CVEs de imagen base antes de desplegar. Se elevó Newtonsoft.Json a 13.0.4 para corregir la dependencia transitiva vulnerable solicitada por Hangfire; conservar esta referencia hasta que una actualización transitiva segura permita retirarla.

Los clientes web/móviles nunca incluyen credenciales de integración. La API confía en backends autenticados para determinar el subjectId; no sustituye autorización de StockMini. El SDK independiente debe denegar Premium tras cacheUntil si no puede actualizar; revocación puede propagarse hasta 30 segundos más tolerancia de reloj, según el diseño.

Las pruebas actuales verifican transporte y metadatos de restricciones. El análisis de dependencias no es un pentest. La matriz SDK/BOLA/concurrencia de fases posteriores sigue pendiente.

Bootstrap verificado con PostgreSQL: ante MFA inválido no persisten usuarios; dos intentos concurrentes crean exactamente un SuperAdmin y un evento de auditoría. No existe registro HTTP. El almacén estándar Identity conserva la clave TOTP en la base: requiere proteger almacenamiento, backups y privilegios; DataProtection no cifra ese token automáticamente. La prevención persistente de replay en login está implementada; recuperación del autenticador sigue pendiente.


Login nunca emite sesión sin MFA. Las cookies se contrastan con sesiones persistentes y SecurityStamp en cada solicitud; logout revoca su registro. TOTP usado se retiene tres minutos y challenge se consume bajo bloqueo de usuario. Contraseña acertada no borra fallos MFA. Rate limits específicos y límite de cuerpo auth 8 KiB complementan lockout. No existe bypass MFA de Development.

