# Pagos manuales y notificaciones

Los pagos registran comprobantes; LicenseHub no cobra ni devuelve dinero. Crear un pago Pending permite adjuntar una intención de renovación inmutable, visible antes de confirmar. Solo un administrador con payments:confirm puede confirmarlo. La confirmación y renovación se ejecutan en una transacción, con operationId permanente, Idempotency-Key y control ETag. Un fallo de renovación conserva el pago pendiente. Rechazar y registrar un reembolso requieren permisos separados y motivo. Refunded no revoca automáticamente la licencia.

Cada consulta y modificación verifica la aplicación real del recurso y los permisos del administrador. Amount es una cadena decimal positiva, hasta cuatro decimales; Currency tiene tres letras mayúsculas. Reference es única por aplicación. No se guardan datos de tarjetas.

Los eventos de licencia se escriben en un outbox transaccional. Hangfire ejecuta reconciliación y despacho cada minuto, registrados mediante --provision-jobs. Las alertas predeterminadas son 7, 3 y 1 día; las licencias cortas usan offsets en segundos (predeterminado 60). La expiración efectiva sigue comprobándose al consultar derechos, aunque el trabajo programado tenga retraso.

Cada entrega se identifica por licencia, versión de derechos, evento, offset y canal. PostgreSQL impide duplicados; los cambios de derechos cancelan entregas pendientes obsoletas. El canal InApp se publica y consulta con GET /api/v1/licenses/warnings, autenticación de aplicación y subjectId resuelto por el backend consumidor. No expone otras aplicaciones ni generaciones antiguas.

El correo usa IEmailSender y un transporte SMTP real, texto plano y STARTTLS con validación de certificado. Configurar Smtp:Host, Port, From, Username y PasswordFile fuera del código. No habilitar AllowUnencryptedDevelopment fuera de Development/Testing. Notifications:Locale selecciona las plantillas (es por defecto). Variables admitidas: {applicationName}, {subjectId}, {licenseId}, {expiresAt}, {eventType}. Se rechazan variables desconocidas y saltos de línea en el asunto.

Sin proveedor configurado, los correos quedan Failed con mail_not_configured; no se simula un envío exitoso. Un destinatario ausente produce Skipped. Se realizan hasta cinco intentos, con espera exponencial de 1, 2, 4 y 8 minutos. Los errores almacenados están redactados. SMTP ofrece entrega al menos una vez: una caída después de la aceptación del proveedor y antes del commit puede causar un correo repetido. Message-ID estable ayuda a identificarlo, sin prometer exactamente una entrega.

La CLI privada --run-notifications procesa una ronda; su salida confirma procesamiento, no entrega de todos los correos. El panel permite revisar registros, configuración por aplicación y versiones de plantillas. Deshabilitar un canal cancela sus entregas pendientes; cambiar offsets afecta nuevas programaciones.

## Verificación ejecutada

67 pruebas Linux/Docker pasadas: 41 integración PostgreSQL, 9 seguridad y 17 unitarias. Incluyen confirmación concurrente, rollback, aislamiento, reintentos, expiración de dos minutos, invalidación de alertas y transporte SMTP real contra un servidor de ensayo local. Trece respuestas HTTP reales validadas con OpenAPI. Angular lint/build correctos (aviso de presupuesto inicial 523.20 kB frente a 500 kB).

El panel se revisó sobre una base aislada con usuario, licencia y comprobante sintéticos. Se confirmó un pago con renovación, se repitió sin duplicación y se guardaron configuración y plantilla. La CLI produjo InApp Sent y Email Failed por ausencia deliberada de SMTP. No se enviaron mensajes al operador. Vista móvil 390×844 sin desbordamiento. Capturas en docs/screenshots/phase7-*.png.
