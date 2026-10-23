# Verificación PWA y navegadores

15 de octubre de 2026. El operador confirmó Windows como plataforma inicial; el hardware Android/iOS/macOS/Linux sigue sin confirmar. No se presentan viewports emulados como dispositivos físicos.

## Automatización Windows ejecutada

`tools/test-windows-pwa.cjs` usa Edge instalado en Windows en modo headless y un perfil temporal independiente. Solo acepta un ensayo sintético local verificado por `tools/test-production-artifact.ps1`, en puertos 8450–8999 y con una cuenta `release-<uuid>@example.invalid`. No usa credenciales, MFA, cookies ni datos del operador. Las solicitudes de página se restringen al origen local; no se guardan trazas de solicitudes ni cabeceras secretas.

Se ejecutaron 15 comprobaciones sobre el frontend con la corrección responsive:

- HTTPS confiable y Angular real; modo oscuro conservado tras recarga.
- Login/MFA sintéticos desde formularios; cookies Secure/HttpOnly/SameSite=Strict.
- Sin desbordamiento horizontal a 360, 390, 768 y 1366 píxeles (viewports emulados).
- Sesión válida después de reiniciar **solo la API sintética**, reutilizando keyring persistente cifrado por PFX.
- Service worker Angular activo; cachés sin entradas API/health.
- Sin conexión: disponibilidad negativa, ninguna respuesta exitosa cacheada de sesión privada y reapertura del shell estático.
- Reconexión: sesión actual obtenida del servidor; logout invalidado también en backend.

La ejecución descubrió un fallo real: el breakpoint móvil sustituía `minmax(0, 1fr)` por `1fr`, dejando que textos largos expandieran el grid. Se conservó el límite de columna, se permitió reducir las tarjetas y partir textos largos. La suite se repitió sobre la corrección, sin ocultar el problema con `overflow:hidden`.

Requiere Node, Playwright y Edge disponibles en el entorno de pruebas; no añade una dependencia de ejecución al producto:

```powershell
$env:LICENSEHUB_PLAYWRIGHT_MODULE = '<ruta local a playwright>'
node tools/test-windows-pwa.cjs '<ruta privada state.json de ensayo verificado>'
```

La prueba reinicia únicamente el contenedor sintético identificado por ese estado. Debe ejecutarse con Docker accesible y un paso TOTP nuevo; no desactiva prevención de replay. Screenshots, versión de navegador y resultados quedan privados en `.tooling/release-tests/<uuid>`.

## Pendientes que requieren interacción/dispositivos reales

El 22 de octubre se repitieron las **15 comprobaciones automatizadas** con Edge 155.0.4283.62 de Windows sobre el frontend corregido; todas pasaron. La conexión interactiva inicialmente enumeró aplicaciones, pero el acceso a Edge venció y los reintentos fallaron con timeout/solicitud activa incluso tras restablecer la sesión. La autorización posterior del operador no resolvió el fallo técnico. La instalación/reapertura/actualización instalada continúa sin verificar; ver [informe del incremento](local-update-2026-10-22.md).

| Plataforma/prueba | Estado |
| --- | --- |
| Windows Edge: navegador automatizado, responsive, MFA, offline/reconexión | Ejecutado; 15 comprobaciones |
| Windows: instalar PWA, abrir como aplicación, cerrar/reabrir y actualizar desde interfaz del navegador | Pendiente manual |
| Windows: expiración de sesión y actualización con formularios sin guardar desde la PWA instalada | Pendiente manual; pruebas backend no sustituyen UX instalada |
| Android, iPhone/iPad, macOS, Linux: navegador/instalación/actualización | Pendientes; dispositivos no confirmados |

Para Windows manual: abrir https://localhost:8443/ con TLS confiable en Edge; instalar desde el menú Aplicaciones si está disponible; iniciar sesión/MFA por el operador sin compartir secretos; abrir/cerrar/reabrir; comprobar tema/navegación y que logout exige nueva autenticación. Probar desconexión/reconexión y verificar indisponibilidad, nunca derechos Premium offline. En una actualización controlada, guardar trabajo antes de usar el aviso de recarga. Registrar SO/navegador/versión/revisión/fecha/resultados; no fotografiar QR MFA, claves ni credenciales.

El control visual interactivo no pudo inicializarse en esta sesión (error de assets del runtime). Se utilizó automatización con Edge real de Windows para las pruebas descritas; eso no acredita instalación manual ni comportamiento de otro SO. No se declararán aprobadas las filas pendientes sin ejecutarlas.

El incremento de usabilidad del 22 de octubre amplió y repitió la prueba con **29 comprobaciones Edge aprobadas**: incluye navegación por aplicación/tarea, creación real de aplicación/función/cliente/licencia, catálogo, preasignación por nombre y protección de códigos, además de las pruebas PWA automatizadas anteriores. Capturas de escritorio claro/oscuro y móvil revisadas; instalación manual continúa pendiente. Ver [usabilidad](admin-usability.md).
