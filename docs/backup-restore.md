# Respaldo y restauración

Para transporte de copias cifradas, reintentos verificados y propuesta de retención sin borrar archivos, ver [backup-storage.md](backup-storage.md). Copia y restauración locales ensayadas; proveedor externo y su recuperación siguen pendientes.

Actualización del 15 de octubre: [migración local ejecutada](local-migration-2026-10-15.md). La herramienta detecta `.secrets/compose/active-database.json` y usa el clúster activo tanto para Backup como para RestoreTest sin destino explícito; falla si el contenedor no coincide con el ID conservado. RestoreTest sigue creando únicamente bases aisladas con sufijo `_tests`. El origen permanece detenido y conservado. No quitar el marcador para solucionar un error de respaldo: podría copiar la base antigua. El destino opcional `-RestoreContainer` sigue limitado a contenedores candidatos locales.

Ensayo local ejecutado el 13 de octubre de 2026 con PostgreSQL 17 existente, sin eliminar ni recrear licensehub. Requiere PowerShell 7 sobre .NET 8 o superior y Docker Desktop. La herramienta está fijada a los contenedores Development locales; no es un servicio general de backup productivo.

```powershell
./tools/backup-restore.ps1 -Mode Backup
./tools/backup-restore.ps1 -Mode RestoreTest -Archive '<archivo privado .lhbk>'
./tools/test-backup-integrity.ps1 -Archive '<archivo privado .lhbk>'
./tools/test-restored-permissions.ps1 -Database '<licensehub_restore_UUID_tests>'
```

El backup incluye pg_dump custom de datos y esquemas negocio/Hangfire, manifest SHA256 por archivo y carpeta DataProtection. AES-256-GCM cifra todo con nonce aleatorio de 12 bytes, tag de 16 bytes y encabezado versionado autenticado como AAD. Clave aleatoria en .secrets/backup-key/aes256.key; archivos cifrados en .secrets/backups. Permisos restringidos al usuario, excluidos de Git, sin imprimir contenido. Temporales en claro se eliminan en finally. Límite 256 MiB; para bases grandes preparar streaming o backup físico/WAL.

La clave está separada en directorios, no en un dispositivo o gestor independiente. Antes de producción custodiarla fuera del host, ensayar su recuperación y controlar accesos. Perderla impide recuperar el backup; comprometer el host permite leer ambos. No subir claves/dumps/archivos privados a CI, GitHub o chat. La conexión local utiliza socket Unix dentro del contenedor, sin contraseña en argumentos; producción necesita autenticación y TLS propios.

RestoreTest autentica antes de crear una base, verifica checksums y crea un nombre aleatorio licensehub_restore_<32 hex>_tests, sin sobreescribir destinos. pg_restore --exit-on-error --no-owner --no-privileges recrea datos/restricciones/índices. SQL verifica hashes, MFA SuperAdmin, constraints e índices válidos y rechazo real de UPDATE sobre auditoría/renovaciones, con rollback. Cuenta tablas y archivos DP. No inicia API/Hangfire/SMTP. La base se conserva para inspección; su eliminación posterior exige revisar el nombre exacto y autorización del operador.

Ensayo reforzado: licensehub_restore_77c923bcef3647f391c5d45f8389060e_tests; 39 tablas, 1 archivo DP, 6 migraciones. SHA256 cifrado: 17C7B47EA5243F099494A87D7EF0D652253F3BFA92019A6D32811A230FAF8733. Archivo manipulado y clave incorrecta rechazados; original preservado. Conteos/tiempos en .tooling/restore-results, fuera de Git. SuperAdmin/fixtures permanecen en base principal.

Roles/permisos se provisionan aparte. docker/runtime-permissions.sql fue ensayado con rol NOLOGIN temporal en restauración y rollback: niega UPDATE auditoría, CREATE esquema y TRUNCATE, conserva DML de licencias/Hangfire. Reaplicar/revisar después de nuevas migraciones.

DataProtection se recuperó y verificó por checksum; no se ensayó una sesión real restaurada ni PFX productivo. TOTP Identity está dentro del dump cifrado. Custodiar PFX actuales/antiguos y secretos por separado antes de producción; esta herramienta local no los incluye. pg_dump es consistente para DB, pero la copia posterior del keyring no es snapshot coordinado: conservar claves/certificados necesarios y ensayar política de sesiones. No iniciar workers de clones con credenciales reales.

Pendiente operacional: acordar RPO/RTO, frecuencia, retención, copia externa y alertas. No se programó backup ni envío externo automático. PITR requiere WAL/backup físico. Registrar tiempos y ensayar periódicamente; decidir invalidación de sesiones/rotación de credenciales ante incidentes.
