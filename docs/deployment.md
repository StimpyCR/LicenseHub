# Preparación de despliegue

Módulos y SDK v1 implementados y verificados localmente. No se autorizó ni realizó despliegue productivo. CI solo comprueba.

docker/compose.yaml es Development. docker/compose.production.yaml es una plantilla independiente con nombre/red/volumen distintos, sin PostgreSQL ni migraciones automáticas; requiere DB ya provisionada con runtime mínimo. No combinar ambas.

Variables: LICENSEHUB_API_IMAGE y LICENSEHUB_WEB_IMAGE (digest aprobado repo@sha256:...), LICENSEHUB_PUBLIC_HOST (dominio real), LICENSEHUB_DB_CONNECTION_FILE, LICENSEHUB_DP_CERTIFICATE_FILE y LICENSEHUB_DP_PASSWORD_FILE (PFX DP/contraseña), LICENSEHUB_TLS_CERTIFICATE_FILE y LICENSEHUB_TLS_KEY_FILE. LICENSEHUB_LISTEN_ADDRESS por defecto 127.0.0.1; puerto 8443. Revisar ingress/443 y no publicar DB. La plantilla exige valores, pero revisar realmente digest/infraestructura antes de ejecutar.

Producción, AllowedHosts explícito, keyring persistente cifrado y proxy nginx explícito. PFX DP independiente de TLS, privado/vigente; conservar versiones antiguas. API/web no root, rootfs solo lectura, capacidades eliminadas, no-new-privileges; escrituras solo tmpfs/keyring. Nginx genera plantilla en tmpfs y añade HSTS/TLS 1.2/1.3. Compose validado estructuralmente; nginx generado y cargado en prueba temporal sin publicar puertos. No acredita DNS/certificados/conexión productivos.

Aplicar migraciones revisadas y --provision-jobs con identidad migradora propietaria separada antes de workers. Runtime NO SUPERUSER/CREATEDB/CREATEROLE/REPLICATION, sin membresía/propiedad migradora. Aplicar docker/runtime-permissions.sql con psql -v runtime_role=... -v database_name=...; revisar permisos tras nuevas tablas. API no migra/provisiona al arrancar.

Verificar HTTPS y /health/ready antes de tráfico; /health/live no consulta DB. Configurar probes en orquestador/monitor: no se añadieron sondas dependientes de curl inexistente. Dashboard Hangfire cerrado. Monitorizar outbox, errores redactados y fallos backup/SMTP.

Readiness exige conexión y migraciones al día. Fuera de Development/Testing también comprueba el rol PostgreSQL: rechaza SUPERUSER, CREATEDB, CREATEROLE, REPLICATION, BYPASSRLS, acceso a archivos/programas del servidor y CREATE en la base o esquemas de aplicación. Revisa roles alcanzables por membresía, incluso sin herencia automática; no basta quitar atributos directos si el runtime puede asumir un rol privilegiado.

Una configuración rechazada produce 503 genérico en /health/ready; /health/live puede continuar respondiendo. El orquestador debe retirar tráfico cuando readiness falla: este control no sustituye las ACL ni bloquea por sí mismo todas las rutas HTTP. No valida exhaustivamente cada permiso de tabla, función SECURITY DEFINER o extensión. Aplicar/revisar runtime-permissions.sql y comprobar el entorno real sigue siendo obligatorio. No compartir la identidad migradora con API/workers.

Development captura en Mailpit (mailpit-development.md). Proveedor real Brevo opt-in con docker/compose.smtp.yaml; pendiente dominio/remitente/clave y no activado. Para producción faltan dominio/TLS, digests/SBOM/escaneo imágenes, custodia DP/backups externa, rol DB efectivo, RPO/RTO/retención/destino/alertas y pruebas PWA en dispositivos. Solicitar autorización explícita sobre configuración concreta antes de desplegar.


Las imágenes actuales de PostgreSQL tienen hallazgos críticos/altos en el análisis local; no aprobarlas para producción sin revisar/remediar y ensayar restauración. No se alteraron los servicios existentes para corregir CVE. Ver security-local-review.md.
