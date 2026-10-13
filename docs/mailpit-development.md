# Correo local de desarrollo

Mailpit está integrado en docker/compose.yaml. Abrir http://127.0.0.1:8025/ para consultar capturas; no exige comprar dominio ni credenciales. El remitente de desarrollo usa example.invalid y SMTP apunta a mailpit:1025. LicenseHub conserva IEmailSender y configuración por entorno.

La captura tiene volumen nuevo mailpit-data, usuario 65532, rootfs de solo lectura, capacidades eliminadas y no-new-privileges. Solo participa en mailcapture, una red internal sin salida. No se configuraron relay, forwarding, webhooks, comprobaciones de versión o DNS inverso. SMTP no tiene puerto publicado. Un proxy nginx no root/solo lectura publica únicamente la UI en loopback 8025 y preserva Host/Upgrade para la interfaz/WebSocket. Mailpit restringe los hosts a localhost/127.0.0.1/mailpit y rechaza hosts ajenos. No expone el buzón a la LAN ni convierte capturas en envíos externos.

La configuración pública de Mailpit y su control de hosts se describen en la [documentación oficial](https://mailpit.axllent.org/docs/configuration/runtime-options/). Se deshabilitó el borrado automático; monitorear tamaño del volumen y borrar mensajes solo por decisión explícita del operador. Una UI local sin autenticación permite acceso a usuarios del mismo equipo: usar exclusivamente datos de prueba. El navegador puede contactar enlaces que el operador decida abrir; no pulsar enlaces externos durante pruebas privadas.

Variables Development: LICENSEHUB_SMTP_HOST (mailpit), LICENSEHUB_SMTP_PORT (1025), LICENSEHUB_SMTP_FROM (notifications@licensehub.example.invalid), LICENSEHUB_SMTP_ALLOW_UNENCRYPTED_DEVELOPMENT (true para captura interna). Production rechaza SMTP plaintext. No usar esos valores para un proveedor real. docker/compose.smtp.yaml es un complemento separado y opt-in que exige TLS y credencial privada; no se activó.

Para arranque completo de desarrollo: tools/start-local.ps1 incluye los servicios nuevos. Para este incremento se añadió únicamente Mailpit/proxy y se actualizó API por el cambio SMTP/red, sin recompilar ni recrear PostgreSQL/web. Sus IDs/montajes se compararon antes/después y el usuario administrativo permanece intacto. Nunca usar compose down -v, borrar volúmenes o recrear base para configurar SMTP.

Prueba repetible desde la raíz:

```powershell
./tools/test-backend-docker.ps1 -WithMailpit
# Solo si la imagen ya contiene las pruebas actuales:
./tools/test-backend-docker.ps1 -WithMailpit -SkipBuild
```

Requiere PostgreSQL de integración existente y su secreto privado, Mailpit iniciado y redes locales. Envía únicamente direcciones aleatorias example.invalid. Crea datos/administrador sintéticos en una base auth_UUID_tests separada que el fixture retira al terminar; no utiliza la cuenta del operador ni modifica fixtures SDK persistentes. Las capturas se conservan, no se limpia el buzón.

El ensayo real ejecutado pasó: Activated, ExpiryApproaching, Expired y Renewed llegaron por SMTP a Mailpit; se leyeron y comprobaron por su API HTTP. Replays de activación/renovación y dispatch repetido no añadieron mensajes, los registros Sent tuvieron un intento y ningún contenido incluyó la clave de licencia. Plantillas personalizadas también renderizaron IDs correctos. Se avanzó el reloj controlado del fixture para aviso/expiración de dos minutos; el servidor real no tiene su reloj alterado. Suite completa: 79 aprobadas, cero fallos/omisiones (49 integración, 17 unitarias, 13 seguridad).

Sin -WithMailpit el caso opt-in aparece explícitamente omitido; no contar esa omisión como evidencia SMTP. CI provisiona Mailpit efímero y activa el caso; no usa credenciales ni datos operativos. Captura local no acredita entrega en bandejas reales ni autenticación con Brevo.

Brevo queda preparado como proveedor futuro, con dominio/remitente autenticados cuando el operador decida disponer de ellos. No se compró dominio ni se envió correo real. Ver smtp-configuration.md.
