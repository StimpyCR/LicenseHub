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

## Alternativa que conserva glibc

Escaneo local completado, sin red ni exportación de inventario: 1 crítica, 63 altas, 110 medias y 132 bajas en el filesystem final. Mejora respecto al origen pero no cumple el objetivo de remediación; no se recomienda desplegarla como solución definitiva.

Se añadió docker/Dockerfile.postgres-glibc-candidate, fijada al digest de la imagen Debian local del origen, sin gosu y con USER postgres. Se construyó sin red. No aplica actualizaciones de paquetes ni sustituye el servicio principal.

tools/postgres-locale-probe.sh se ejecutó realmente sobre origen y candidata en clusters sintéticos efímeros: red deshabilitada, filesystem de solo lectura, sin capabilities y con datos únicamente en tmpfs. Coincidieron los tres resultados:

```text
a,A,á,ä,n,ñ,z,Z
áäñi|ÁÄÑI
a01,a1,a-b,a.b,a_b,ab
```

Estos vectores comprueban ordenamiento y conversión de caracteres concretos; no sustituyen una regresión completa ni prueban todas las collations. El código actual ordena Slug, Code, NormalizedEmail y FeatureCode, y utiliza lower para buscar clientes: un cambio de libc sí tiene efectos posibles sobre comportamiento observable. No se modificaron esas consultas ni el contrato v1.

El [seguimiento oficial Debian](https://security-tracker.debian.org/tracker/CVE-2026-6653) sigue marcando vulnerable la rama estable para el problema XML consultado el 13 de octubre de 2026. Por eso eliminar gosu no basta para declarar la alternativa remediada. Los detalles del inventario y resultados del escaneo permanecen privados. Esta candidata todavía no tiene restauración ni suite completa ejecutadas; las 79 pruebas anteriores corresponden a Alpine.

## Continuación del 15 de octubre: verificación reproducible

Se añadió compare-postgres-locales.ps1 para comparar realmente origen fijado y candidata mediante los tres vectores sintéticos, sin red ni volúmenes persistentes. Guarda evidencia local ignorada y falla si los resultados difieren. La sintaxis PowerShell se verificó; el nuevo wrapper aún no se ejecutó contra Docker. Las ejecuciones manuales del 13 de octubre descritas arriba sí se completaron.

La receta glibc distingue el target base (sin actualizaciones, ensayo del 13) del target patched, que solicita actualizaciones de los repositorios estables configurados en esa imagen. El segundo es experimental: no se construyó ni escaneó todavía. No atribuirle los conteos de la primera ni asumir que corrige el problema XML. Su resolución depende del estado de los repositorios y requiere fijar el artefacto aceptado, escaneo actualizado y regresión antes de una migración.

```powershell
docker build --target base --network none -f docker/Dockerfile.postgres-glibc-candidate -t licensehub-postgres:glibc-candidate .
# Descarga solo paquetes públicos de la distribución, sin montar secretos/inventarios.
docker build --target patched -f docker/Dockerfile.postgres-glibc-candidate -t licensehub-postgres:glibc-patched .
./tools/scan-images-local.ps1 -UpdateDatabase -Images licensehub-postgres:glibc-patched
./tools/compare-postgres-locales.ps1 -Candidate licensehub-postgres:glibc-patched
```

Docker Desktop inició sus procesos el 15 de octubre, pero el motor Linux no respondió y WSL reportó docker-desktop detenido. Por ello estas nuevas pruebas quedaron pendientes; no se restableció Docker, reinició WSL ni modificó ningún contenedor o volumen. La base pública del escaneo anterior corresponde al 13 de octubre y debe actualizarse antes del siguiente dictamen. Procedimiento de cambio/retorno: [postgres-migration-runbook.md](postgres-migration-runbook.md).

### Reintento completado el 15 de octubre

Docker volvió a responder (29.8.2). La variante patched se construyó realmente: apt descargó índices de los repositorios configurados y actualizó dos paquetes, sin instalar ni eliminar otros. El wrapper compare-postgres-locales.ps1 se ejecutó sobre origen fijado y candidata: los tres vectores coinciden. La evidencia queda en .tooling/postgres-locale/latest.json; esto no acredita compatibilidad general.

Se descargó únicamente la base pública de avisos UpdatedAt 2026-10-15T01:13:01Z. Después, Trivy analizó la imagen local con red deshabilitada, sin telemetría ni exportación de inventario. Resultado del filesystem final: **1 crítica, 61 altas, 109 medias, 138 bajas**. No comparar estos conteos directamente con el ensayo del 13 como medida de parches corregidos: cambió también la base de avisos. Los detalles permanecen privados en .tooling/security.

La actualización estable disponible no cumple el objetivo de remediación: no se recomienda desplegar esta candidata. No se repitió la suite completa ni una restauración sobre ella; las 79 pruebas documentadas siguen correspondiendo a la candidata Alpine anterior. El contenedor PostgreSQL original conserva su ID y se encontró detenido: no se arrancó, sustituyó ni modificó su volumen. Las siguientes decisiones deben resolver los hallazgos restantes conservando la semántica o evaluar una alternativa con un cambio de comportamiento explícito y regresión suficiente. Fase 10 abierta.
