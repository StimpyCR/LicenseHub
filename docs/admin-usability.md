# Panel administrativo: navegación por tareas

El panel mantiene una aplicación abierta y una sección visible a la vez. La pantalla inicial ofrece tarjetas de aplicaciones y un filtro explícitamente limitado a la página actual; la paginación conserva el alcance del servidor. Dentro de cada aplicación hay accesos para crear licencias, gestionar clientes y consultar/renovar, además de pagos, correo, conexión e informes. La administración de usuarios tiene su propio espacio.

Al registrar una aplicación se propone el slug a partir del nombre, eliminando acentos y caracteres incompatibles. Se puede corregir antes de crear; editar manualmente detiene la sugerencia automática. Después de crear sigue siendo inmutable. Desactivar conserva historial y bloquea la autenticación de las integraciones; no se añadió eliminación.

Clientes, listado de licencias y emisión tienen pestañas separadas. La preasignación usa nombres de clientes devueltos por la API para la aplicación actual; conserva paginación/búsqueda, sin inventar una lista completa. El catálogo muestra funciones activas y permite seleccionar sus nombres. Si hay más funciones que las cargadas se informa y siguen disponibles los códigos avanzados. El servidor mantiene la validación final de aplicación, permisos y catálogo.

Las nuevas licencias proponen **30 días desde la activación**; las opciones de minutos, horas, meses calendario, años, fecha específica y vitalicias siguen disponibles. Es una preferencia del formulario, sin cambiar DTO, valores predeterminados del servidor ni contratos del SDK. Los estados se traducen para presentación conservando los valores JSON de OpenAPI v1.

Cambiar de aplicación/sección o salir del navegador advierte sobre borradores y códigos de un solo acceso. Cambiar durante una operación se bloquea. Cerrar sesión sigue disponible. No se persisten códigos ni credenciales en localStorage; se borran al destruir su componente. La navegación interna de clientes/emisión conserva los formularios en memoria; al salir de emisión se confirma y ocultan los códigos emitidos.

Los módulos se cargan bajo demanda. Las consultas de componentes para proteger borradores usan referencias de plantilla para conservar esa carga diferida. Docker limita Angular a dos workers para reducir presión sobre el entorno compartido y excluye la caché Angular local del contexto de construcción.

## Verificación

La evidencia ejecutada y la aplicación al entorno local se registran al cerrar el incremento. Las pruebas de navegador usan exclusivamente un entorno Production sintético aislado, HTTPS confiable, MFA, API real y PostgreSQL separado. No se automatiza el acceso a la cuenta del operador ni se fotografían códigos o credenciales.

La prueba Windows ahora cubre slug automático, cancelación de salida con borrador, creación real de aplicación/cliente/licencia, selección de cliente por nombre, duración propuesta, protección de códigos al navegar, sección única y anchuras 360/390/768/1366. Conserva las pruebas de autenticación, cookies, tema, disponibilidad, service worker, offline, reconexión y cierre de sesión. La instalación manual de PWA continúa pendiente.

## Resultado del incremento del 22 de octubre de 2026

- TypeScript y formato correctos; build de producción Angular en Windows y Docker aprobado. Persiste el aviso conocido de presupuesto inicial: 520,61 kB frente a 500 kB (sin ampliar el presupuesto para ocultarlo).
- 11 comprobaciones Production del ensayo base aprobadas. Tras el último ajuste exclusivo del frontend se sustituyó solo su contenedor sintético y se aprobaron **29 comprobaciones Edge** sobre la imagen final, incluyendo creación de función y selección del catálogo. La prueba inicial se corrigió para esperar la carga diferida del módulo de pagos antes de comprobar su visibilidad; no se atribuye ese fallo de sincronización al backend.
- Revisión visual de capturas reales de escritorio claro/oscuro y móvil. Sin desbordamiento en 360, 390, 768 y 1366 px; esto es emulación de viewport, no prueba de dispositivos físicos.
- Trivy local offline de la imagen final: cero críticas, altas, medias y bajas; gate de identidad/frescura aprobado. Sin exportar inventarios.
- Frontend aplicado en `https://localhost:8443/`, imagen `sha256:a013017fa26da7c06fc96bd60a2084d319f670c1c43eab47f815a24df8299c4a`. Live/ready, bundles, CSP y recursos PWA correctos. API, contenedor/volumen de base activa y contenedores StockMini conservados; no se ejecutaron migraciones ni se cambiaron sus imágenes.
- La comprobación adicional de 12 consultas sobre la fixture principal **no pasó**: ambas aplicaciones de esa fixture están desactivadas y sus credenciales reciben 401 aunque no hayan vencido. Se verificó el estado por consulta de solo lectura y no se reactivaron. La compatibilidad/aislamiento se comprobó en el entorno sintético activo; este resultado no acredita las 12 consultas principales.
- Retirados únicamente los tres contenedores, dos volúmenes y dos redes del ensayo, su imagen intermedia y credenciales sintéticas. Conservadas evidencia ligera, imagen activa y anterior.

Para volver al frontend anterior, conservar trabajo pendiente y restaurar **solo** `webImage` desde el marcador privado `.secrets/compose/active-before-ux-20261022.json` al marcador activo; ejecutar `tools/start-migrated-local.ps1` y `tools/verify-local.ps1`. La imagen anterior sigue disponible: `sha256:47b7175de39a27f88256e4a47ded7a913fa0836573e666808a0660f5ac47d4f1`. Esta reversión no requiere restaurar la base. El mecanismo automático de reversión ante fallo de aplicación quedó preparado; no se afirma un nuevo ensayo de rollback completo en este incremento.

La Fase 10 global conserva sus pendientes externos/manuales; este incremento cierra la mejora de uso del panel local, no esos requisitos.
