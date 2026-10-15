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

## Readiness y privilegios runtime: 15 de octubre

API configura la comprobación reforzada fuera de Development/Testing. Además de conectividad y migraciones, rechaza atributos administrativos, permisos de archivos/programas del servidor y capacidad de CREATE en base/esquemas, directamente o por roles alcanzables. El healthcheck devuelve el 503 genérico existente; liveness no cambia. No se alteran DTO, credenciales, contrato OpenAPI ni ACL existentes.

Compilación Linux/Docker sin red: 0 warnings y 0 errores; formato aplicado sobre los tres archivos cambiados. Suite final ejecutada: **90/90 aprobadas**, 60 integración + 13 seguridad + 17 unitarias, ninguna omitida. Los once casos nuevos prueban superusuario frente a comportamiento Development, rol acotado aceptado, CREATEDB/CREATEROLE/BYPASSRLS/REPLICATION, CREATE en esquema/base, acceso a archivos del servidor y membresías administrativas/de creación con INHERIT FALSE.

Se usaron PostgreSQL y Mailpit nuevos en redes internas nuevas, sin puertos publicados, datos sintéticos y contraseña privada con ACL restringida. Roles y grants de cada caso se revierten con la transacción; las bases auth_UUID_tests siguen el ciclo de vida de la fixture existente. No se modificaron cuenta del operador, datos, permisos ni contenedores anteriores. Contenedores/redes nuevos y evidencia privada .tooling/runtime-role-tests se conservaron; no hay envíos SMTP reales.

La primera compilación detectó EF1002 en la preparación de roles de prueba. Se corrigió utilizando identificadores entrecomillados generados internamente y comandos transaccionales; la compilación final pasó sin suprimir el analizador. Primera suite 88/88, ampliada y repetida tras agregar casos indirectos: 90/90. No se afirma que el escaneo de imagen haya mejorado: PostgreSQL sigue pendiente de remediación y no se desplegó la API nueva sobre el entorno existente. Falta aplicar y validar un rol runtime real durante el despliegue autorizado.

## Control de artefactos y consolidación operativa

CI c3ffa60 confirmado success, run 38762135571. Añadidas 14 pruebas Python independientes, todas pasadas, al control local de identidad/hash/vigencia/estructura/severidad de informes. Recorridos reales con escaneos nuevos y avisos del 15: Alpine aceptada por el control con 0 críticas/altas y 1 media, Debian patched rechazada con 1 crítica/61 altas. La aceptación de escaneo no resuelve la diferencia de locale Alpine. Corregida la distinción entre ID de manifiesto Docker e ID de configuración Trivy mediante evidencia de exportación ligada por hash; no se relajó la comprobación a comparar etiquetas.

No cambios de backend/SDK en este incremento: no se repitieron sus suites locales porque cambiaron únicamente herramientas/documentación/CI. OpenAPI, compatibilidad, sintaxis PowerShell y diff correctos. Los informes/evidencia privados no se suben a CI. El usuario aún no eligió alojamiento ni destino externo; no se programaron copias externas, compras o despliegues. release-readiness.md identifica las condiciones de cierre reales y el protocolo PWA físico pendiente.
