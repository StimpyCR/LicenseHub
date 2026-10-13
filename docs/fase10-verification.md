# Fase 10: evidencia y pendientes

Avance verificado el 13 de octubre de 2026; la fase no está cerrada ni hay despliegue productivo.

## Ejecutado

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

- Gmail exclusivo: cuenta/remitente verificados y contraseña de aplicación privada; prueba de entrega solo cuando el operador autorice un destinatario. Ahora SMTP sigue sin habilitarse. Ver smtp-configuration.md.
- Dominio/infraestructura reales, certificados TLS/DP y custodia de versiones antiguas, rol runtime efectivo y pruebas de readiness en su entorno. No se reutiliza el superusuario Development para producción.
- RPO/RTO, retención, programación y copia externa cifrada/clave en custodia independiente, alertas y ensayo sobre el destino elegido. Se conservan las bases aisladas para inspección; no se eliminaron datos.
- Escaneo de imágenes/SBOM por digest: Docker Scout está instalado, pero la revisión automática rechazó ejecutar CVE porque puede exportar inventario de paquetes de imágenes privadas a un servicio externo no aprobado. No se sorteó el bloqueo; se solicitó autorización específica. No afirmar que las imágenes están libres de CVE.
- Pruebas de instalación PWA en Windows/Android/macOS/iOS/Linux y recuperación de sesiones/certificados en infraestructura objetivo. La revisión responsive previa y los recursos PWA no acreditan dispositivos reales ni pentest independiente.

La automatización de CI no despliega. Completar y revisar configuración concreta antes de solicitar autorización productiva. Este commit debe identificarse como avance, no cierre de fase.
