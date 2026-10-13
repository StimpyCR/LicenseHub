# Candidata PostgreSQL: evaluación aislada

Evaluación del 13 de octubre de 2026. No sustituye postgres del Compose, no modifica su volumen ni autoriza una migración. El servidor actual conserva sus hallazgos y necesita remediación.

Se construyó docker/Dockerfile.postgres-candidate sobre PostgreSQL 17 Alpine fijado por digest. El entrypoint admite USER postgres, por lo que se elimina gosu y se inicia sin root. No se descartan resultados ni se suprimen CVE. El binario eliminado no aparece en el filesystem final; las capas históricas de la imagen base permanecen en el artefacto y no se afirma que hayan sido borradas.

Trivy sin red/telemetría: candidata final con 0 críticas, 0 altas, 1 media y 0 bajas. El resultado corresponde a componentes del filesystem final y a la base de avisos del ensayo; no certifica ausencia de vulnerabilidades desconocidas. Paquetes/CVE/image IDs detallados permanecen solo en .tooling/security, fuera de Git.

Se creó un servidor nuevo no root (UID70), rootfs solo lectura, cap_drop ALL y no-new-privileges, con volumen/red nuevos y sin puertos publicados. Se restauró el backup AES-GCM real en una base licensehub_restore_UUID_tests: 39 tablas, 1 archivo DataProtection, constraints/índices/hashes/MFA y rechazo de modificaciones de auditoría/historial comprobados. No se arrancaron API, SMTP ni workers sobre ese clon. El usuario/secretos restaurados no se usaron para las pruebas.

Las 79 pruebas completas se ejecutaron en bases sintéticas separadas dentro de la candidata: 49 integración, 17 unitarias y 13 seguridad, todas aprobadas sin omisiones. Mailpit conservó las capturas anteriores. El ensayo detectó un defecto en la prueba SMTP: total cuenta todo el buzón; messages_count cuenta coincidencias. Se corrigió para verificar destinatario exacto y contar mensajes filtrados, sin borrar el buzón.

## Diferencia que impide una sustitución automática

Origen y candidata informan locale en_US.utf8 con proveedor libc, pero glibc y musl no ordenan igual. Vector público de comparación:

| Origen glibc | Candidata musl |
| --- | --- |
| a,A,á,ä,n,ñ,z,Z | A,Z,a,n,z,á,ä,ñ |

No hay una garantía general de compatibilidad de búsquedas, collations, extensiones ni índices por pasar la suite actual. Revisar las operaciones dependientes de locale y elegir explícitamente conservar semántica glibc o adoptar una política distinta con pruebas de regresión. No montar el volumen actual en esta imagen: cambia también el UID del proceso. Una eventual migración debe ser lógica a un volumen nuevo, con escritura/worker pausados, backup fresco, validación, autorización concreta y retorno al origen definido.

## Repetir sin tocar origen

```powershell
docker build --network none -f docker/Dockerfile.postgres-candidate -t licensehub-postgres:candidate .
./tools/scan-images-local.ps1 -Images licensehub-postgres:candidate
./tools/test-postgres-candidate.ps1 -Archive '<backup cifrado privado .lhbk>'
```

Requiere Docker, imagen actual licensehub-tests:phase10-mailpit, Mailpit/red mailcapture y key privada del backup. Los secretos nuevos se generan criptográficamente en .secrets/postgres-candidate con ACL restringida. La restauración permite únicamente nombres de contenedor candidato con UUID y no habilita destinos arbitrarios. Los informes/TRX se mantienen privados. Los recursos aislados se conservan para diagnóstico; no se borran datos/volúmenes como parte del ensayo. Fallos iniciales de argumentos/tmpfs se corrigieron antes del ensayo aceptado; los recursos vacíos iniciales también permanecen.

Siguiente decisión: resolver la semántica de locale y preparar un cambio revisable. No se pide ni se infiere aprobación para cambiar el servidor principal basándose solo en un menor conteo de CVE.
