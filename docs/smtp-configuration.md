# Correo configurable

El proveedor actual de desarrollo es Mailpit: captura local sin comprar dominio, sin credenciales y sin envíos externos. Reemplaza la planificación anterior de Gmail. Abrir http://127.0.0.1:8025/. Ver mailpit-development.md para topología, persistencia y pruebas reales.

Compose Development configura Smtp__Host=mailpit, Smtp__Port=1025 y remitente example.invalid. LICENSEHUB_SMTP_HOST/PORT/FROM y LICENSEHUB_SMTP_ALLOW_UNENCRYPTED_DEVELOPMENT permiten configurar el transporte. Plaintext está permitido únicamente para captura interna de Development/Testing; Production lo rechaza. No hay claves SMTP en el repositorio.

Brevo permanece preparado como proveedor futuro, sin activarlo. Su [documentación oficial](https://developers.brevo.com/docs/supabase-smtp-integration) describe smtp-relay.brevo.com y puerto 587 con STARTTLS. docker/compose.smtp.yaml exige remitente real verificado, usuario y LICENSEHUB_SMTP_PASSWORD_FILE. Mapea Smtp__PasswordFile al Docker secret; la contraseña no se imprime ni se almacena en .env versionado. El operador pospuso dominio autenticado: no comprar uno, crear credenciales ni enviar correo real durante estas pruebas.

Cuando exista dominio/remitente autorizado, preparar su configuración privada, comprobar TLS/autenticación y revisar outbox antes de permitir un destinatario real expresamente autorizado. No usar el correo personal SuperAdmin como prueba. El archivo privado debe restringir lectura al usuario/servicio; revocar credenciales antiguas al rotar.

Prueba repetible: ./tools/test-backend-docker.ps1 -WithMailpit. MailpitDeliveryTests envía eventos de activación, aviso previo, expiración y renovación desde application services/outbox/SmtpEmailSender a Mailpit real; consulta su API y comprueba destinatario aislado, plantillas, una captura por evento y ausencia de claves. Usa PostgreSQL aislado y reloj controlado, conserva las capturas. También se mantiene SmtpTransportTests con listener local y rechazo de SMTP sin STARTTLS cuando es obligatorio.

La captura local no acredita entrega real, reputación/deliverability ni autenticación Brevo. Entrega SMTP al menos una vez: fallar entre aceptación y commit puede repetir mensajes. Revisar Failed/Skipped, reintentos y atraso antes de activar otro proveedor. Mailpit no tiene relay/forwarding ni salida de red; es exclusivamente desarrollo.
