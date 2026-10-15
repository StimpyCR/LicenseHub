# Propuesta revisable: PostgreSQL con locale builtin

15 de octubre de 2026. Estado: **probada en aislamiento, pendiente de aceptación y migración autorizada**. No cambia la base actual ni autoriza producción. Complementa postgres-candidate.md y postgres-migration-runbook.md.

## Alternativa concreta

docker/Dockerfile.postgres-builtin-candidate parte de PostgreSQL 17 Alpine fijado por digest, actualiza paquetes desde la rama estable, elimina el helper innecesario al arrancar sin root y fija initdb con proveedor builtin, locale C.UTF-8, encoding UTF8. El proceso corre como postgres, UID70. No se usa el volumen existente, que corresponde a otro UID/distribución.

[PostgreSQL 17 documenta pg_c_utf8](https://www.postgresql.org/docs/17/collation.html) como ordenamiento por puntos de código y mapeo Unicode simple para lower/upper; su comportamiento es estable dentro de la versión mayor. [initdb documenta la selección del proveedor builtin](https://www.postgresql.org/docs/17/app-initdb.html). Una versión mayor futura exige otra regresión; esta propuesta no implica estabilidad indefinida.

## Evidencia ejecutada

- Imagen local nueva licensehub-postgres:builtin-candidate, sin sustituir las anteriores. Escaneo Trivy sin red ni telemetría, avisos del 15 de octubre: **0 críticas, 0 altas, 0 medias, 0 bajas y sin severidades desconocidas**. El control de identidad/hash/vigencia la acepta. No prueba ausencia de vulnerabilidades desconocidas ni elimina los archivos de capas históricas de la imagen base.
- Dos clústeres sintéticos con builtin (imagen Debian de referencia y candidata final Alpine) coinciden en seis vectores SQL y una huella SHA256 del mapeo lower/upper de los **1 112 063 puntos de código representables por text** examinados. Se excluyen NUL y surrogates; la comparación es una huella diagnóstica, no una demostración formal sobre toda operación Unicode.
- Backup AES-GCM real existente del 13 de octubre restaurado en volumen nuevo: **39 tablas, 1 archivo Data Protection**, integridad y restricciones comprobadas. No se arrancaron API, workers ni SMTP sobre esa base restaurada.
- Suite actual completa en bases sintéticas separadas de la candidata: **90/90 aprobadas**, 60 integración + 13 seguridad + 17 unitarias, sin omisiones. Incluye correo capturado en Mailpit de pruebas. Se comprobó el proveedor efectivo b; no se infirió de los argumentos solamente.
- Secretos y evidencia detallada permanecen en .secrets/.tooling. El contenedor original, sus datos, permisos y volumen no se modificaron. La cuenta/MFA del operador no se utilizó para pruebas ni se restableció.

La receta apk upgrade resuelve paquetes del repositorio vigente; no garantiza reconstrucción binaria idéntica. Una migración debe usar **el artefacto exacto probado**, identificado privadamente y comprobado por el control de imagen, sin reconstruirlo entre ensayo y corte. No se publicó imagen en ningún registry ni se autorizó producción. La suite independiente SDK de Fase 9 no se repitió aquí.

## Cambios observables que requieren aceptación

La propuesta **no conserva la semántica completa del locale libc en_US.utf8 actual**:

| Ejemplo | Actual | Propuesta |
| --- | --- | --- |
| Letras y acentos | a,A,á,ä,n,ñ,z,Z | A,Z,a,n,z,á,ä,ñ |
| Códigos válidos | a1,a-a,a.a,a_a,aa,advanced_reports | a-a,a.a,a1,a_a,aa,advanced_reports |

La revisión exhaustiva encontró **54 puntos de código** cuyo lower o upper difiere entre el proveedor actual de la imagen de referencia y pg_c_utf8. Las búsquedas sintéticas de i y de %_ coinciden, pero eso no acredita equivalencia para esos otros caracteres: pueden cambiar coincidencias de búsqueda de clientes. Los resultados de listados por texto también pueden variar.

Identificadores UUID, hashes, comparación exacta de subjectId, estados, claves y datos permanecen sin transformación. Las operaciones de licencia no cambian DTO, endpoints, autenticación ni reglas de vigencia. El contrato SDK trata los códigos de features como conjunto, sin orden cultural prometido. Aun así, **no se presenta como transición semánticamente transparente**: antes de adoptarla se debe aceptar el nuevo comportamiento administrativo. Si se exige conservar exactamente la búsqueda/orden anterior, no aplicar esta propuesta; revisar otra alternativa o una evolución explícita de API según las garantías requeridas.

Durante el corte, los usuarios deben reiniciar listados/paginación y no continuar offsets/cursors previos sobre el nuevo orden. La paginación actual no ofrece un snapshot entre despliegues. No se cambió silenciosamente el contrato congelado ni su SHA256.

## Migración local propuesta, todavía no ejecutada

1. Obtener autorización para iniciar/pausar servicios locales y cambiar su conexión. Registrar configuración/IDs y estado originales de forma privada.
2. Pausar API/workers que escriben. Iniciar solo el PostgreSQL de origen si estaba detenido. Crear **backup cifrado fresco** y verificar integridad; el archivo del ensayo no sustituye ese paso.
3. Crear otro contenedor/volumen nuevo con la imagen exacta aprobada y builtin. Restaurar el backup fresco mediante el flujo ya verificado, validar y dejar la base lista bajo su nombre operativo. No montar ni alterar físicamente el volumen original.
4. Provisionar propietario/migrador separado y rol runtime mínimo en el destino nuevo. Conservar la cuenta del operador, MFA y keyring existentes; no generar otra identidad SuperAdmin ni resetear credenciales. Actualizar solo la conexión privada necesaria para el corte.
5. Comprobar HTTPS/readiness, permisos e integración antes de habilitar escrituras. Conservar origen, configuración y backup para retorno. Después de admitir nuevas escrituras, no volver automáticamente al origen atrasado.

Esta autorización sería **local**, no un despliegue productivo ni una compra. La ejecución debe revisar nombres y condiciones concretas antes de cada acción; no se ofrece un comando destructivo automático. No se eliminarán contenedores o datos anteriores. La política de permisos, backup externo, TLS/DP del alojamiento elegido y pruebas físicas PWA siguen pendientes del destino/dispositivos.

## Repetir el ensayo sin cambiar origen

```powershell
docker build -f docker/Dockerfile.postgres-builtin-candidate -t licensehub-postgres:builtin-candidate .
./tools/scan-images-local.ps1 -Images licensehub-postgres:builtin-candidate
./.venv/Scripts/python.exe tools/check_image_security.py licensehub-postgres:builtin-candidate
./tools/compare-postgres-locales.ps1 -Candidate licensehub-postgres:builtin-candidate -LocalePolicy builtin -FullUnicodeCaseMapping
./tools/test-postgres-candidate.ps1 -Archive '<backup cifrado privado>' -CandidateImage licensehub-postgres:builtin-candidate -LocalePolicy builtin -TestImage licensehub-tests:runtime-role -MailpitNetwork '<red interna de Mailpit de pruebas>'
```

La imagen de pruebas runtime-role se construyó localmente con el código de las 90 pruebas actuales. La red de pruebas admitida tiene nombre licensehub-runtime-<32 hex>-tests, o licensehub-local_mailcapture; no admite redes externas arbitrarias. Los recursos de ensayo se conservan y no publican puertos. Las opciones por defecto de las herramientas anteriores mantienen la política libc previa: builtin requiere selección explícita o la nueva imagen; el ensayo verifica el proveedor solicitado y falla si no coincide.
