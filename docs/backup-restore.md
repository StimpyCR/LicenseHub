# Respaldo y restauración

Procedimiento operacional propuesto, aún no ejecutado. Definir RPO/RTO y retención con el responsable del sistema antes de producción. Respaldar PostgreSQL (datos de LicenseHub y esquema Hangfire), archivos DataProtection y certificados de descifrado necesarios. Guardar claves de cifrado separadas del backup, con control de acceso y trazabilidad. No poner secretos ni dumps en Git ni en artefactos públicos de CI.

Usar pg_dump formato custom con conexión desde .pgpass protegido o secret manager; no contraseña literal en comandos. Cifrar el archivo con una herramienta aprobada y clave administrada antes de enviarlo al destino de backup. Verificar checksum, registrar tamaño/fecha/resultado y monitorear fallos. Para bases grandes, evaluar backups físicos y WAL/PITR con el operador; pg_dump por sí solo no proporciona recuperación a cualquier instante.

Restaurar primero en una base aislada: descifrar con autorización operacional, pg_restore con roles revisados, aplicar migraciones de la versión correspondiente y comprobar esquema, conteos, constraints, auditoría, canje/renovación e idempotencia. Deshabilitar email/ workers salientes durante ensayo para no enviar notificaciones reales. Recuperar DataProtection y certificados para verificar sesiones según política; considerar invalidar sesiones y rotar credenciales tras incidente.

Registrar tiempo de restauración y evidencia del resultado; limpiar copias descifradas según política. La falta de ensayo de restauración impide declarar preparación productiva. Esta guía no es evidencia de un respaldo existente ni de una restauración exitosa.
