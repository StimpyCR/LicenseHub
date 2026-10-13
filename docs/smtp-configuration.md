# Correo configurable

El operador eligió Gmail exclusivo para notificaciones, separado de su correo personal; reemplaza Brevo como proveedor previsto. Remitente/contraseña siguen pendientes: no se inventaron ni habilitaron envíos. IEmailSender mantiene transporte desacoplado.

Configuración acordada: Smtp__Host=smtp.gmail.com, Smtp__Port=587, STARTTLS con Smtp__AllowUnencryptedDevelopment=false. Smtp__From/Smtp__Username serán la cuenta real verificada. Contraseña desde Smtp__PasswordFile privado restringido o archivo inyectado por secret manager. No se admite Smtp__Password directamente por entorno; el archivo evita exponer valor en Docker inspect.

Complemento opcional docker/compose.smtp.yaml: LICENSEHUB_SMTP_HOST/PORT/FROM/USERNAME y LICENSEHUB_SMTP_PASSWORD_FILE. start-local.ps1 no lo carga. Validar config --quiet y revisar outbox antes de activarlo: una actualización permitiría enviar mensajes pendientes. No se ejecutó mientras falta cuenta verificada.

No utilizar correo personal del SuperAdmin para pruebas ni enviar claves por chat/logs/comandos. Archivo privado, nunca Git/CI; rotación revocando clave previa en proveedor. Confirmar con Google disponibilidad/requisitos de contraseña de aplicación cuando se configure la cuenta.

Prueba local: ./tools/test-backend-docker.ps1. SmtpTransportTests abre captura TCP loopback efímera con example.invalid, comprueba Message-ID/texto plano y rechazo del servidor sin TLS cuando STARTTLS es obligatorio. NotificationTests verifica outbox, deduplicación concurrente, avisos/expiración dos minutos, reintentos/redacción/configuración ausente y autorización. No contacta Gmail ni destinatarios externos. No demuestra autenticación/entrega Gmail; se probarán tras verificar cuenta y autorizar un destinatario.

Entrega al menos una vez: puede duplicar tras aceptación previa y fallo antes de commit. Revisar Failed/Skipped, límites/reintentos y atrasos antes de habilitar correo; no prometer exactamente una vez.
