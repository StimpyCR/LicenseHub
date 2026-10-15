# Migración local verificada — 15 de octubre de 2026

Estado: **realizada y activa**, exclusivamente en desarrollo local. El operador autorizó el corte y aceptó las diferencias de texto documentadas bajo condición de no pérdida, conflictos de unicidad ni fallos funcionales. No se desplegó producción ni se eliminaron contenedores, volúmenes, respaldos o datos originales.

## Respaldo y conservación

- Con API/workers originales detenidos, se creó `.secrets/backups/20261015T163047Z-44e35bd56eb340c0a8d96e5997325f81.lhbk` (AES-GCM). SHA256: `A3B872776D4019CF39016704907453E32E3C89CD1AF20DBB362E377F3B06C767`.
- Se restauró ese archivo fresco en otro contenedor y **otro volumen Docker independiente**, con el artefacto builtin exacto previamente probado. Se verificaron 39 tablas y 1 archivo Data Protection. El keyring operativo se conserva; no se restablecieron usuario, contraseña ni MFA del operador.
- Antes del corte, huellas SHA256 de todas las filas de las **39 tablas** coincidieron. El cálculo fija UTC y orden binario explícito; no depende del orden de filas del nuevo locale. La restauración reconstruyó índices únicos y restricciones sin conflictos; las comprobaciones de FK, checks, índices válidos e historial inmutable pasaron.
- Se compararon **218 definiciones** de índices/restricciones. Cinco tienen una representación SQL diferente: cast de `varchar[]` a `text[]` frente a casts de cada literal del array a `text`. Son la misma comparación sobre los mismos literales; al normalizar solo esa expresión exacta, todas las definiciones coinciden. No se alteraron predicados ni valores permitidos.
- Se creó además un respaldo de la base migrada: `.secrets/backups/20261015T164243Z-08ae78cc099e48ab9324316b607daf0b.lhbk`, SHA256 `BF50C2A7FE029634990AE2EC0A00353F986557E3E98565A77829E7489B7BBD4E`. Su restauración aislada volvió a verificar 39 tablas/1 DP. Manipulación y clave incorrecta fueron rechazadas, conservando el archivo original.

Los archivos cifrados y su clave permanecen privados y excluidos de Git. Falta elegir custodia independiente fuera de este equipo; una copia local no cubre la pérdida del equipo.

## Texto y contrato

Se mantiene la política aceptada de [postgres-builtin-proposal.md](postgres-builtin-proposal.md): orden de texto distinto y **54 puntos Unicode** con diferencias de lower/upper. La comparación exhaustiva y seis vectores se habían ejecutado sobre el artefacto exacto. En los clientes existentes, **cero filas** presentan cambios de lower en SubjectId, DisplayName o Email. La igualdad de todas las huellas acredita que no se transformaron valores almacenados. Las pruebas de licencia/aislamiento no encontraron incompatibilidades funcionales.

Los listados deben reiniciarse después del cambio de orden; no continuar paginación anterior al corte. Los cambios futuros en búsquedas de esos caracteres siguen siendo observables y aceptados, no una promesa de equivalencia cultural con el origen.

OpenAPI v1, DTO, estados, HTTP, Bearer opaco, scopes, aplicación derivada del token e idempotencia **no cambiaron**. El contrato congelado y su SHA256 permanecen iguales.

## Verificaciones ejecutadas

| Verificación | Resultado |
| --- | --- |
| Suite completa sobre la restauración fresca, antes del corte | 90/90: 60 integración, 13 seguridad, 17 unitarias; cero omisiones |
| Suite completa después del cambio, en bases sintéticas separadas del mismo clúster | 90/90, cero omisiones; incluye activación concurrente/idempotente, vigencia, renovaciones, pagos y correo Mailpit |
| Respuestas HTTP reales de la suite contra OpenAPI | 15/15 válidas |
| SDK existente desde el host hacia HTTPS local | 7/7, cero omisiones: TLS, token ausente/aleatorio/revocado, consulta autenticada, licencia expirada/revocada |
| Consultas adicionales HTTPS con fixtures dedicadas | 12/12: aislamiento A/B, features, estados, vitalicia, warnings y scopes |
| Frontend y sondas | live/ready 200 Healthy, Angular/bundles/CSP/PWA servidos con TLS confiable |
| Rol de ejecución | LOGIN sin privilegios administrativos ni membresías; permisos DML necesarios y restricciones de auditoría/esquema verificados |
| Control de imagen con evidencia local reciente | Cero hallazgos reportados en PostgreSQL; imagen exacta comprobada, sin exportar inventario |
| Herramienta de control de imagen | 14/14 pruebas Python |
| Contrato, compatibilidad y sintaxis PowerShell | Correctos |

Las dos ejecuciones de 90 son la misma suite, no 180 casos distintos. Las siete del SDK son una selección real posterior al corte; **no se afirma haber repetido toda la suite SDK** ni las 18 antiguas pruebas live, cuyas licencias finitas pueden haber expirado naturalmente. No se usaron credenciales del SuperAdmin.

La API y el frontend locales reutilizan sus imágenes existentes fijadas por ID privado; esta operación migra PostgreSQL y endurece su ejecución, **no publica una nueva versión de la API**. La suite de 90 utiliza el código actual compilado en su imagen de pruebas. Un despliegue posterior de código requiere su propia verificación del artefacto final.

## Operación local actual

- Entrada: **https://localhost:8443/**. Mailpit: **http://127.0.0.1:8025/**. No se usa 8444.
- PostgreSQL nuevo no publica puerto al host. PostgreSQL, API y web activos usan raíz de solo lectura, sin capabilities adicionales y sin privilegios de root.
- API usa `licensehub_runtime`, no el propietario/migrador. Hangfire quedó habilitado: se verificó su registro/heartbeat y no se detectaron marcadores de error en el arranque revisado. SMTP apunta exclusivamente a Mailpit; no hay envíos reales.
- Origen: contenedores PostgreSQL/API/web **detenidos y conservados con sus IDs y volúmenes originales**. Mailpit y sus capturas se conservan. La base nueva conserva el nombre de contenedor del ensayo al ser promovida; su nombre operativo de base es `licensehub`.
- `.secrets/compose/active-database.json` identifica privadamente contenedor/imagen/volumen/conexión activos. Su configuración anterior y el registro del corte están en `.secrets/local-migration` y `.tooling/local-migration`. No publicar esos archivos.
- `tools/start-local.ps1 -SkipBuild` detecta el marcador y delega en `start-migrated-local.ps1`; se verificó que no recrea ni reactiva el origen. El launcher exige contenedor, imagen y volumen validados, comprueba HTTPS y conserva las imágenes exactas. `SkipBuild` no reconstruye el entorno migrado.
- `tools/backup-restore.ps1 -Mode Backup` usa la base activa y su keyring. RestoreTest crea siempre una base `licensehub_restore_<uuid>_tests`, nunca sobrescribe `licensehub`. Sin destino explícito, usa el clúster activo. Para Docker Desktop, la transferencia temporal usa un directorio privado en el volumen nuevo y retira únicamente sus propios temporales.

No ejecutar directamente `docker compose -f docker/compose.yaml up` sobre esta instalación migrada: es la plantilla del origen y no el launcher actual. Las reconstrucciones/migraciones futuras deben planificarse expresamente; el launcher preserva el corte comprobado.

## Reversión: qué se comprobó y qué hacer ahora

**Ensayo ejecutado antes de admitir tráfico autenticado o workers:** con POST/PUT/PATCH/DELETE bloqueados y Jobs=false, se detuvo la API nueva, se cambió su conexión privada por la original conservada, se inició y se obtuvo HTTPS ready 200 Healthy. Luego se volvió a la conexión nueva y se obtuvo 200 Healthy. Tras ambos pasos, las huellas de las 39 tablas de origen y destino seguían idénticas a las anteriores. Esto prueba retorno de conexión y disponibilidad; no es una prueba de interacción manual del operador con MFA.

**Ahora ya existen escrituras en destino** (LastUsedAt de credenciales de prueba y trabajos programados). El origen está atrasado: no arrancarlo como fallback automático. Para volver:

1. Cerrar el acceso de escritura del proxy y detener `licensehub-local-next-api-next-1` para congelar también Hangfire. Conservar el marcador y los dos archivos de conexión; no borrar contenedores ni volúmenes.
2. Ejecutar Backup con el marcador todavía activo. El keyring se copia también desde la API detenida. Restaurar ese respaldo en una base aislada, comprobar integridad y determinar cambios desde el corte con las huellas/evidencia privadas.
3. Reconciliar o restaurar los cambios del destino bajo un procedimiento revisado. No sustituir el destino por el volumen antiguo si eso descarta operaciones. En caso de duda mantener los escritores detenidos y reportar el conflicto.
4. Solo con ausencia de divergencia acreditada o recuperación conciliada, iniciar el PostgreSQL original, usar la conexión original conservada con Jobs=false y comprobar HTTPS/readiness como en el ensayo. Mantener un único grupo de escritores.
5. Tras aceptar la recuperación, actualizar el marcador/launcher a la instalación que realmente se usará, verificar autenticación, aislamiento, tareas y respaldo, y entonces reabrir escrituras. Conservar ambos lados y los respaldos.

La reversión previa a escrituras fue ensayada; no se promete una reversión automática sin pérdida después del corte. La Fase 10 sigue abierta por alojamiento, custodia externa de respaldos y pruebas PWA físicas; ningún destino productivo fue inventado ni autorizado.
