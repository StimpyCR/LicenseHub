# Fase 10: aceptación local y pendientes de salida

15 de octubre de 2026. El operador decidió continuar en Docker local, posponer elección de alojamiento y almacenamiento externo y comenzar por Windows. No se contrataron servicios, no se desplegó producción ni se enviaron datos/secretos/inventarios privados a servicios de análisis. La Fase 10 global **permanece abierta** hasta verificar los pendientes explícitos; el siguiente paquete local está implementado y comprobado.

## Artefactos y entorno Production sintético

Se construyó la API actual con restore bloqueado y publish Release y el frontend con npm ci/build. Los contextos Docker excluyen secretos, claves y evidencia privada. El frontend actualiza paquetes estables Alpine durante build y termina ejecutándose con UID101; no se actualizaron paquetes dentro de contenedores activos. La receta de actualización no es reproducibilidad binaria: conservar/fijar el artefacto exacto y repetir el control de imagen después de reconstruirlo.

`tools/test-production-artifact.ps1` resuelve imágenes locales a sus IDs antes del ensayo y crea recursos nuevos `licensehub-release-<uuid>-tests`: PostgreSQL y keyring independientes, migraciones explícitas, propietario separado y `licensehub_runtime` sin privilegios administrativos. Solo utiliza datos sintéticos, incluido un SuperAdmin con TOTP generado privadamente. La preparación de fixtures se hace por CLI sin servidor público; no habilita esa CLI en Production. No copia cuenta/MFA/datos del operador.

API ejecuta realmente `ASPNETCORE_ENVIRONMENT=Production`, AllowedHosts explícito, PFX Data Protection privado y runtime mínimo. API/DB están en red interna; solo el proxy tiene una red de entrada y publica HTTPS en loopback. Los tres procesos son no root, rootfs solo lectura, cap_drop ALL/no-new-privileges. No hay SMTP real configurado ni workers para notificaciones de estos fixtures.

**11 comprobaciones reales aprobadas** sobre la imagen final:

1. HTTPS normal con readiness Healthy y usuario mínimo.
2. Sondas con no-store.
3. Angular servido con HSTS.
4. POST administrativo sin CSRF rechazado.
5. Administración anónima rechazada.
6. Activación vitalicia y replay idempotente con el runtime restringido.
7. Aislamiento entre dos aplicaciones autenticadas.
8. Readiness 503 cuando el runtime alcanza un rol administrativo, sin revelar diagnósticos privados.
9. Readiness recuperado a 200 tras retirar esa membresía.
10. Cookie CSRF segura.
11. Keyring persistente cifrado por PFX, sin master key en claro.

El ensayo usa el template nginx productivo y los controles reales del artefacto; no acredita un host cloud, DNS/certificado público ni la instalación de Compose en otro proveedor. No se cambió la instalación principal en este incremento: estos artefactos y el arreglo responsive se verificaron en la instalación sintética. El panel principal sigue en https://localhost:8443/ con su configuración conservada.

En una revisión final del entorno principal, readiness agotó el timeout de 15 segundos después de ejecutar los ensayos. Sin reiniciar ni modificar esa instalación, una consulta posterior devolvió 200 Healthy y la verificación completa habitual volvió a pasar (live/ready, Angular, bundles, CSP y recursos PWA). El diagnóstico privado registró cancelación de operación; no se estableció una causa ni se atribuye automáticamente a un defecto o a carga. Registrar/monitorizar recurrencia y medir recursos/carga en el destino elegido; los aprobados funcionales locales no certifican capacidad ni SLA. Los ensayos sintéticos anteriores se detuvieron conservando contenedores/volúmenes y el ensayo final sigue separado del principal.

Durante preparación se detectaron y corrigieron dos fallos del arnés: Docker Desktop no publicaba el puerto de un proxy conectado únicamente a red interna y una aserción trataba una cabecera PowerShell como escalar. Se conservó evidencia de los intentos; no se reportaron como aprobados. La ejecución final completa pasó.

## Seguridad de imágenes

Análisis Trivy sin red/telemetría, avisos públicos del 15 de octubre y gate ligado al hash/ID del artefacto:

| Artefacto ensayado | Críticas | Altas | Medias | Bajas |
| --- | ---: | ---: | ---: | ---: |
| API actual | 0 | 0 | 14 | 5 |
| Frontend final actualizado | 0 | 0 | 0 | 0 |
| PostgreSQL builtin de la migración local | 0 | 0 | 0 | 0 |

Se corrigió un hallazgo medio del frontend con una actualización estable disponible y se repitieron escaneo y ensayo. Los 19 hallazgos de la API no tienen FixedVersion indicado en la base utilizada; no se suprimieron ni se interpretó eso como ausencia de riesgo/parche futuro. Antes de salida real requieren reevaluación con avisos recientes y revisión operativa de riesgo/alcanzabilidad. No se afirma que un escaneo sustituya pentest o demuestre ausencia de vulnerabilidades desconocidas. Detalles e inventarios permanecen privados en `.tooling/security`; Docker Scout sigue opcional, no autorizado y sin ejecutar.

## Windows, responsive y recuperación

Ver [evidencia y límites PWA](pwa-verification.md): **15 comprobaciones automatizadas Windows Edge**, con perfil temporal, MFA sintético, cuatro viewports, cookies, reinicio API, offline/reconexión, caché sin datos privados y logout. Se corrigió un desbordamiento real ante textos largos. La instalación/actualización manual de PWA no se infiere de headless.

Ver [copias cifradas](backup-storage.md): transporte explícito, verificación de hashes, reintentos sin sobrescritura, retención solo propuesta y **restauración real de una copia** (39 tablas/1 DP). Nueve pruebas nuevas de copia/retención y catorce de escaneo: **23/23 aprobadas**. No se configuró un proveedor externo ni se borró archivo alguno del operador.

La suite backend de 90 pruebas y siete pruebas SDK HTTPS pasaron durante la migración inmediatamente anterior, sin omisiones; no se presentan como otra ejecución en este incremento. Aquí no cambió código backend ni contrato: se verificó además su imagen publicada con las once pruebas reales de Production. OpenAPI y compatibilidad congelados se volvieron a validar. CI repite backend, frontend, contrato y herramientas. La compilación Angular conserva un aviso de presupuesto inicial de unos 24 kB sobre el objetivo de 500 kB; no se elevó el presupuesto para ocultarlo.

## Condiciones de cierre aún no ejecutadas

| Pendiente | Motivo y siguiente paso |
| --- | --- |
| Alojamiento real, DNS/TLS y verificación en destino | Proveedor no elegido; conservar configuración portable y autorización de despliegue separada |
| Copia externa y recuperación desde ella | Destino no elegido; acordar autenticación/cifrado, frecuencia, retención, custodia independiente, alertas y RPO/RTO y ejecutar recuperación |
| PWA instalada Windows | Ejecutar protocolo manual de instalar/abrir/reabrir/actualizar y expiración de sesión con trabajo sin guardar |
| Android/iOS/macOS/Linux reales | Hardware no confirmado; ejecutar matriz y registrar limitaciones, sin llamar dispositivos a viewports |
| Artefactos de salida y riesgo residual | Repetir escaneo/aceptación en artefacto y destino elegidos; revisar hallazgos medios/bajos de API |

El correo externo sigue pospuesto por decisión explícita; Mailpit cubre desarrollo. Ninguno de estos pendientes se marca como aprobado ni se inventa un destino para cerrar una casilla. No hace falta contratar servicios para continuar desarrollo local, pero la preparación local no constituye una verificación productiva.

## Repetir el ensayo

```powershell
docker build -f docker/Dockerfile.api -t licensehub-api:phase10-rehearsal .
docker build -f docker/Dockerfile.web -t licensehub-web:phase10-rehearsal .
./tools/scan-images-local.ps1 -Images @('licensehub-api:phase10-rehearsal','licensehub-web:phase10-rehearsal')
./tools/test-production-artifact.ps1 -Port 8495
# Con el state privado del ensayo aprobado y Playwright/Edge locales:
node tools/test-windows-pwa.cjs '<state.json privado>'
python -m unittest discover -s tools -p 'test_*.py'
```

Puerto debe estar libre y las imágenes preexistir. El script conserva recursos/evidencia nuevos para diagnóstico; no borra ni recrea la instalación anterior. No ejecutar como un despliegue productivo ni compartir el directorio de fixtures. Las claves/certificados sintéticos son solo para este ensayo y no deben reutilizarse en producción.
