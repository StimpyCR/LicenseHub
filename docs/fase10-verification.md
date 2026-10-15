# Fase 10: evidencia y pendientes

Avance verificado el 13 de octubre de 2026; la fase no está cerrada ni hay despliegue productivo.

## Verificación inicial (conservada como historial)

- Compilación Release de backend en Windows y Linux/Docker: cero errores/advertencias.
- Suite completa Linux con PostgreSQL de pruebas existente: 77 aprobadas, cero fallos/omisiones. Después se ejecutó nuevamente toda seguridad: 13 aprobadas incluyendo un caso adicional, dando 78 pruebas distintas (48 integración, 17 unitarias, 13 seguridad). Cuatro casos nuevos rechazan AllowedHosts comodín, ausencia de certificado DataProtection y SMTP plaintext en Production; el caso positivo lee la contraseña desde archivo privado y comprueba XML DataProtection cifrado con PFX sintético. Captura SMTP loopback/rechazo de servidor sin STARTTLS y outbox/reintentos/deduplicación ejecutados; sin enviar correos externos.
- OpenAPI/ejemplos y compatibilidad con ambos snapshots validados; 15 respuestas HTTP reales de esta suite compatibles con contrato. Ningún cambio al contrato SDK v1 congelado ni a su hash.
- Respaldo AES-256-GCM real y restauración en PostgreSQL existente, sin modificar base principal: 39 tablas, 1 archivo DP, 6 migraciones, 1 usuario, 2 aplicaciones, 9 licencias/activaciones, 1 renovación. Restauración reforzada en 13,50 segundos, incluyendo conteos y comprobaciones; no equivale a RTO productivo. Identificadores/checksum y límites en backup-restore.md.
- Integridad autenticada: archivo manipulado y clave incorrecta rechazados, original preservado. Hashes internos comprobados y UPDATE real rechazado por triggers de auditoría/renovaciones. Ningún worker o SMTP iniciado en clones.
- Rol runtime NOLOGIN temporal en restauración: lectura/DML requerido, UPDATE auditoría denegado, sin CREATE/TRUNCATE/escritura de migraciones. Transacción revertida: no cambia usuarios/permisos del entorno principal.
- NuGet con transitivas y npm audit nivel low: sin vulnerabilidades reportadas en esta consulta; informes privados en .tooling.
- HTTPS local con validación normal de certificado (se retiró SkipCertificateCheck de verify-local): live/ready saludables, Angular/bundles/CSP/PWA correctos.
- Plantilla Compose producción validada estructuralmente con valores de ejemplo; nginx realmente generado por envsubst y validado con nginx -T en contenedor no root/solo lectura, sin publicar puertos. Una primera comprobación reveló tmpfs no escribible; se corrigió antes de aceptar el resultado.
- Formato C# verificado, scripts PowerShell parseados y diff revisado; archivos privados bajo .secrets/.tooling excluidos de Git.

El runtime actualmente desplegado es Development; los guards productivos se verificaron en pruebas. Las plantillas nuevas no se activaron. El SDK no cambió ni se repitió su suite de 94, previamente aprobada en fase 9, porque no se alteraron contratos ni comportamiento de integración.

## Pendiente

- Brevo real sigue pendiente de dominio/remitente autenticados y credencial privada; no comprar dominio ni enviar correo ahora. Ver mailpit-development.md y smtp-configuration.md.
- Dominio/infraestructura reales, certificados TLS/DP y custodia de versiones antiguas, rol runtime efectivo y pruebas de readiness en su entorno. No se reutiliza el superusuario Development para producción.
- RPO/RTO, retención, programación y copia externa cifrada/clave en custodia independiente, alertas y ensayo sobre el destino elegido. Se conservan las bases aisladas para inspección; no se eliminaron datos.
- Escaneo local Trivy ejecutado sin red/telemetría y sin exportar inventarios. PostgreSQL actual: 2 incidencias críticas y 87 altas; pendiente revisión/remediación autorizada. No se modificó ese contenedor. API/web sin críticas/altas; Mailpit sin incidencias. Informes detallados privados en .tooling/security y resumen en security-local-review.md. Docker Scout opcional y no autorizado por decisión explícita.
- Pruebas de instalación PWA en Windows/Android/macOS/iOS/Linux y recuperación de sesiones/certificados en infraestructura objetivo. La revisión responsive previa y los recursos PWA no acreditan dispositivos reales ni pentest independiente.

La automatización de CI no despliega. Completar y revisar configuración concreta antes de solicitar autorización productiva. Este commit debe identificarse como avance, no cierre de fase.


Actualización: Mailpit/proxy añadidos y configuración SMTP/red de API actualizada dentro del alcance solicitado; PostgreSQL/web conservaron IDs/montajes. Suite actual completa 79 (49 integración, 17 unitarias, 13 seguridad), sin fallos/omisiones. Lockfiles NuGet/npm analizados offline sin incidencias. Las bibliotecas del SO en imágenes tienen hallazgos: no confundir con cero vulnerabilidades en dependencias gestionadas. CI reemplaza auditoría npm automática/online por Trivy offline; informes privados de imágenes no se publican.


Evaluación posterior PostgreSQL: candidata aislada no root, 0 críticas/altas y 1 media; backup real restaurado y 79 pruebas aprobadas. Se detectó diferencia de collations glibc/musl, por lo que origen no se cambia y fase continúa abierta. Ver postgres-candidate.md.

15 de octubre: candidata Debian con actualizaciones estables construida; comparación automatizada de tres vectores de locale aprobada. Escaneo sin red con avisos actualizados: 1 crítica/61 altas, remediación insuficiente. No nueva suite completa sobre esta variante, no migración y no cambios del PostgreSQL original (encontrado detenido). Se documentó procedimiento de cambio/retorno; no es una ejecución ni autorización de despliegue.
