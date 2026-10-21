# Limpieza Docker local — 20 de octubre de 2026

Ejecutada por solicitud del operador para retirar recursos innecesarios de LicenseHub. Se revisaron IDs, montajes, referencias y configuración activa; no se ejecutó una poda global ni se retiraron recursos de StockMini o LicenseHub.SDK. No se modificaron credenciales, MFA, conexión ni imágenes de la instalación activa.

## Recursos conservados

Cinco contenedores activos:

- `licensehub-postgres-candidate-9dbcea178d6b406ea71c677b06a8ba32-tests`: **base operativa actual**, aunque conserva el nombre del ensayo durante su promoción. No eliminarlo por contener candidate/tests.
- `licensehub-local-next-api-next-1` y `licensehub-local-next-web-next-1`.
- `licensehub-local-mailpit-1` y `licensehub-local-mailpit-ui-1`.

Un contenedor detenido conservado: `licensehub-local-postgres-1`, con la base anterior para reversión. Volúmenes protegidos: base activa, `licensehub-local_postgres-data`, `licensehub-local_data-protection` y `licensehub-local_mailpit-data`. Las imágenes usadas por cualquier contenedor también se protegieron.

Se conservaron respaldos, clave AES, keyring, certificados y evidencia privada. Antes de borrar, se creó y verificó criptográficamente el respaldo `20261021T031740Z-6b85c9b133264be0859b76ec1fda44f2.lhbk` (fecha del nombre en UTC). No se borraron bases dentro del clúster activo.

## Recursos retirados

- **26 contenedores** antiguos/sintéticos, incluidas las API/web anteriores al corte y los ensayos Production, runtime, integración y candidatas descartadas.
- **16 volúmenes** de ensayo, uno de ellos huérfano. Se verificó ausencia de referencias antes de cada eliminación; los volúmenes compartidos protegidos se excluyeron.
- **16 redes** de ensayo vacías.
- **17 imágenes** exclusivas de LicenseHub sin referencias en ningún contenedor. No se borraron imágenes base ni imágenes etiquetadas por otro proyecto.
- Caché: selección por IDs y dependencias de compilaciones identificadas como LicenseHub, recuperables y no compartidas. Se excluyeron SDK, StockMini y registros en uso. Docker reportó **14,41 GB recuperados**. El primer filtro conservador no recuperó espacio; no se presentó como exitoso.

Inventario, plan preciso y resultados permanecen privados en `.tooling/docker-cleanup-20261020`. No se suben inventarios a Git ni a servicios externos. Los ensayos retirados deben reconstruirse/prepararse con recursos nuevos para repetirse; sus antiguos archivos de estado no significan que esos contenedores todavía existan.

## Arranque y verificación

Antes de la limpieza, la API no podía servir: había terminado al intentar conectar mientras PostgreSQL seguía arrancando, causando 502 en el proxy. Se corrigió el launcher migrado para esperar consultas reales a PostgreSQL antes de iniciar API, recargar nginx tras posibles cambios de IP y aceptar que las API/web antiguas ya no existan.

Se verificaron arranque después de retirar esos contenedores, `/health/live` y `/health/ready` con 200 Healthy, Angular/bundles/CSP y recursos PWA por HTTPS. Acceso: https://localhost:8443/; Mailpit: http://127.0.0.1:8025/. No se crearon fixtures ni se ejecutaron pruebas completas que volvieran a acumular servidores.

## Espacio Windows y disco virtual

Docker pasó de 32 a **6 contenedores LicenseHub**. La caché total del builder bajó de 30,83 a 16,42 GB; las imágenes totales del daemon de 11,97 a 5,353 GB. Son métricas de Docker con capas compartidas: no sumarlas como espacio físico liberado en Windows.

Tras la limpieza, C: seguía con unos **0,78 GiB libres** y `docker_data.vhdx` con longitud aproximada de **42,35 GiB**. Recuperar espacio dentro de Docker no prueba que el archivo virtual se haya reducido. No se compactó ni se detuvo el Docker compartido: eso requiere una ventana expresamente autorizada porque afecta también a StockMini. [Microsoft explica el almacenamiento VHD de WSL](https://learn.microsoft.com/en-us/windows/wsl/disk-space); cualquier mantenimiento del disco debe conservar los datos y comprobar recuperación/servicios.

Estado de la compactación en este informe: **pendiente de autorización**, no ejecutada como parte de la limpieza exclusiva de LicenseHub.
