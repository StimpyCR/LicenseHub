# Limpieza de autenticación

IAuthMaintenance elimina hashes de códigos MFA cuando ExpiresAt es anterior al reloj UTC, desafíos que expiraron hace más de un día y sesiones que expiraron hace más de 30 días. No elimina usuarios, roles, auditoría ni sesiones/desafíos vigentes. Transacción y lock asesor PostgreSQL hacen seguros los reintentos concurrentes.

Comando privado, con ConnectionStrings__LicenseHubFile configurado para la base correcta y los requisitos de entorno habituales:

```text
dotnet run --project backend/src/LicenseHub.Api -c Release --no-launch-profile -- --cleanup-auth
```

No inicia servidor HTTP. Imprime solo éxito o auth_cleanup_failed. No equivale a borrar auditoría ni al procedimiento de recuperación.

Si Jobs__Enabled=true, la API registra auth-cleanup como tarea recurrente horaria UTC en Hangfire con almacenamiento PostgreSQL persistente. Antes de habilitarlo, el operador debe provisionar el esquema Hangfire con una identidad de migración y probar su ejecución. PrepareSchemaIfNecessary=false impide que el runtime modifique automáticamente ese esquema.

El provisionamiento explícito usa --provision-jobs: no inicia servidor HTTP, prepara el esquema con la identidad operativa, registra la tarea horaria y encola una ejecución para verificar el worker. tools/start-local.ps1 realiza ese paso después de las migraciones EF y antes de arrancar los servicios; Compose habilita Jobs__Enabled=true. La API normal mantiene PrepareSchemaIfNecessary=false. Producción debe usar identidad de provisionamiento separada y no ejecutar automáticamente el script de desarrollo.

La prueba de integración ejecuta la limpieza dos veces en una base aislada y confirma que conserva usuarios, auditoría y registros vigentes. La verificación operativa se documenta en progress.md. No se afirma ejecución en producción.

Verificado localmente el 12 de octubre de 2026: esquema provisionado, worker activo, ejecución encolada con estado Succeeded persistido, tarea recurrente con cron horario UTC y cuenta administrativa conservada. No hay dashboard Hangfire público.
