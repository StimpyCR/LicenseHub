# Actualización local de LicenseHub — 22 de octubre de 2026

Registro del incremento local de Fase 10. No es un despliegue productivo ni cierra los requisitos externos o de dispositivos. El SDK y su contrato v1 no cambiaron. Docker conserva el almacenamiento en D:.

## Correcciones y preparación

- Las dependencias bloqueadas tenían seis hallazgos de Vite 8.3.0: uno alto, cuatro medios y uno bajo. Se añadió un override temporal a **8.3.5**, porque la herramienta Angular fijaba exactamente la versión anterior. El lockfile incorpora sus dependencias transitivas y PostCSS 8.5.31. No se actualizó Angular ni se añadió Vite como dependencia de la aplicación. Revisar/eliminar el override cuando la herramienta adopte una versión corregida y repetir las verificaciones. [Release oficial](https://github.com/vitejs/vite/releases/tag/v8.3.5).
- Se nombró `runtime` a la etapa final del Dockerfile web. Para actualizar sus paquetes sin reutilizar una capa antigua, construir con `docker build --pull --no-cache-filter runtime -f docker/Dockerfile.web -t <etiqueta-nueva> .`. El nombre de etapa no actualiza por sí solo paquetes: sigue siendo necesario ejecutar esa construcción y analizar el artefacto exacto. Una reconstrucción intermedia reutilizó paquetes antiguos de Alpine y fue rechazada; nunca se promovió a la instalación principal.
- El verificador de integración ahora detecta explícitamente credenciales vencidas antes de consultar y admite un timeout configurable, predeterminado de 60 segundos. La credencial de prueba anterior venció el 20 de octubre y la API respondió correctamente 401. Se conservaron el archivo y las credenciales anteriores; se emitieron tres credenciales nuevas mediante los servicios auditados de la CLI privada, limitadas a las mismas aplicaciones de prueba. No se modificaron MFA, contraseña ni permisos del operador.
- El ensayo Production espera readiness HTTPS hasta un máximo de dos minutos. Un primer intento agotó 15 segundos; se conservaron sus resultados y se reanudaron las comprobaciones sobre el mismo entorno sintético después de corregir la espera. No se omitió TLS ni se aceptó una respuesta distinta de 200 `Healthy`.

## Verificaciones ejecutadas

| Verificación | Resultado |
| --- | --- |
| Backend, PostgreSQL y Mailpit aislados | **90/90**: 17 unitarias, 13 seguridad y 60 integración; ninguna omitida |
| Respuestas HTTP de esa suite | **15** compatibles con OpenAPI v1 |
| OpenAPI, ejemplos y compatibilidad congelada | Aprobados; contrato sin cambios |
| Herramientas Python | **23/23**; la repetición final utilizó temporal propio después de un error de permisos del temporal del sandbox |
| Frontend corregido | Compilación y lint aprobados; permanece el aviso de presupuesto inicial existente |
| Imagen final en Production sintético | **11** comprobaciones reales, incluidas activación/replay, aislamiento, permisos mínimos, CSRF y DataProtection cifrado |
| Edge real en Windows, perfil temporal | **15** comprobaciones sobre el artefacto final: MFA sintético, cuatro viewports, tema, sesión tras reinicio, service worker, offline/reconexión y logout |
| Integración HTTPS con credenciales renovadas | **12** consultas: estados, vitalicia, aislamiento, scopes y autenticación |

La suite SDK de 94 pruebas pertenece a Fase 9 y **no se presenta como repetida** aquí. Las consultas actuales y los ensayos de API no equivalen a publicar otro paquete SDK.

## Seguridad local de los artefactos

Avisos públicos actualizados el 22 de octubre; análisis Trivy sin red/telemetría sobre archivos locales y gate ligado al ID y hash del informe. No se utilizó Docker Scout ni se exportaron inventarios.

| Elemento | Críticas | Altas | Medias | Bajas |
| --- | ---: | ---: | ---: | ---: |
| API nueva | 0 | 0 | 21 | 5 |
| Frontend final | 0 | 0 | 0 | 0 |
| Siete lockfiles NuGet/npm, incluido desarrollo | 0 | 0 | 0 | 0 |

Los hallazgos de la API no indican versión corregida en la base utilizada; no se suprimieron. Requieren seguimiento y reevaluación antes de producción. PostgreSQL no se reemplazó ni se presenta su análisis histórico como un escaneo nuevo de esta fecha.

## Resultado del cambio local

**Actualización completada y verificada**, con trabajos programados reactivados. Se mantuvieron el mismo PostgreSQL, volumen, conexión, keyring y configuración de Mailpit. Solo se recrearon API/panel para cambiar sus imágenes; no se aplicaron migraciones de esquema.

Con los escritores detenidos se generó el respaldo cifrado fresco `20261022T191333Z-f9db53eeddbd4856be2f8aaca10b4209.lhbk`. Se restauró realmente en una base aislada: **39 tablas y un archivo de DataProtection**, huellas completas coincidentes. Después se verificaron secuencialmente imagen nueva, retorno real a la anterior y reaplicación de la nueva, con trabajos pausados y las 39 huellas iguales. Al final se restauró la configuración original de trabajos y pasaron HTTPS, Angular, recursos PWA y las 12 consultas de integración. La base temporal de restauración se retiró después de verificarla; el respaldo se conserva y su clave está separada.

El marcador privado `.secrets/compose/active-database.json` conserva las imágenes finales por ID; los launchers existentes lo usan en siguientes arranques. Las imágenes originales y una copia del marcador anterior permanecen disponibles para reversión. Para repetir un retorno: crear respaldo fresco, pausar escritores, restaurar el marcador privado `active-database.before.json` de esta ventana y ejecutar `tools/start-migrated-local.ps1`; verificar salud/datos antes de aceptar tráfico. No restaurar una base antigua sobre cambios posteriores ni borrar volúmenes para cambiar de imagen.

Los contenedores, volúmenes y redes sintéticos creados durante las pruebas se retiraron, al igual que las cuatro etiquetas desechables de imágenes de prueba/intermedias. Se conservaron evidencia, imágenes activas, imágenes originales de reversión, bases compartidas y recursos de otros proyectos. No se hizo una poda global.

## Integración y material privado

`StockMini Integration Test` conserva el UUID `dde7150c-d78b-4c30-8258-2441bf4cc9d7`. Las credenciales vigentes para esta verificación se guardan en `.secrets/sdk-integration/local-update-validation.json`, con vencimiento el **29 de octubre de 2026, 18:59 UTC**. Los secretos no se imprimen ni se versionan. Este archivo no actualiza automáticamente las variables privadas de un backend consumidor.

Para repetir las consultas, desde el host y con TLS confiable:

```powershell
./tools/verify-local-integration.ps1 -FixtureFile .secrets/sdk-integration/local-update-validation.json
```

El archivo predeterminado anterior se conserva: si sus credenciales están vencidas, el verificador lo indicará. No alargar la vigencia alterando la base ni desactivar la autenticación para superar una prueba.

## Evidencia y límites

Los registros privados están en `.tooling/local-update-c9fd5bcdf55d4fd1a380388f85ddb6d7`; el ensayo final y sus capturas sintéticas en `.tooling/release-tests/4710042cd93e471093e0c14cd592ffb5`. El primer intento de actualización volvió automáticamente a las imágenes originales cuando falló la comprobación con las credenciales antiguas. No se contabilizó como una actualización completada.

La instalación interactiva de PWA, reapertura instalada y actualización con trabajo sin guardar **siguen pendientes**. El operador autorizó el acceso a Edge, pero el control de Windows falló con timeout y solicitud activa incluso después de restablecerlo. Las pruebas headless no acreditan esa instalación ni dispositivos Android/iOS/macOS/Linux. No se enviaron correos reales, se contrataron servicios ni se operó sobre StockMini.
