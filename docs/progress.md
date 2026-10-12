# Estado por fases

1. Arquitectura/modelo/contrato: diseñados; contrato ampliado y parser/fixtures ejecutados. Baseline de desarrollo guardado; aún no hay release v1 publicada ni validación con generador C# del SDK.
2. Base del proyecto: solución .NET 10, separación de capas, persistencia EF/Identity/PostgreSQL y migración inicial, Angular Material/PWA y Dockerfiles/compose. Compilación y verificaciones locales documentadas en README. Migración aplicada y prueba de integración pasada en PostgreSQL 17 mediante Docker. Imágenes API/web construidas y Compose completo iniciado: sondas live/ready saludables por HTTPS, recursos Angular/PWA servidos y usuarios de runtime no root verificados. Scripts de arranque y verificación en tools/. Fase 2 verificada localmente; no constituye validación productiva.
3. En curso: bootstrap privado de SuperAdmin implementado como CLI, con contraseña Identity, TOTP verificado, transacción/lock PostgreSQL, rechazo de reinicialización y auditoría. Login/MFA HTTP, sesión persistente, CSRF y logout implementados y probados. Panel conectado con formularios reactivos. Cuenta real inicializada mediante el flujo privado y acceso confirmado por el operador. Incorporados esquema Bearer de integración, validación persistente de credenciales y permisos administrativos emparejados por aplicación. Pendientes: recuperación de administrador y aplicar las políticas a los futuros endpoints de negocio. Gestión/emisión de credenciales corresponde a fase 4.
4. En curso: API de creación/listado/edición de aplicaciones y emisión/listado/revocación de credenciales, con permisos, CSRF, ETag, idempotencia y auditoría. Formulario de aplicaciones conectado. Pendientes catálogo Premium e interfaz de credenciales.
5–8. Pendientes: clientes, emisión y activación de licencias, renovaciones/revocaciones, pagos/outbox/correo y panel completo.
9. Contrato y guía permiten diseño paralelo del SDK en repositorio independiente; pruebas HTTP de compatibilidad pendientes de API real.
10. CI preparado, guías operativas propuestas; pendiente ejecutar workflow en GitHub, pruebas completas de seguridad y restauración. No despliegue a producción.

Verificación Docker (2026-10-11): cliente y motor 29.8.2 operativos. PostgreSQL 17 iniciado en contenedor aislado con contraseña aleatoria fuera del código. Prueba de integración: 1 pasada, 0 fallos y 0 omisiones. La prueba aplica la migración y verifica aislamiento por claves foráneas y unicidad de activación; no prueba aún endpoints ni activaciones concurrentes.






Verificación del incremento auth: 29 pruebas pasadas (16 unitarias Linux/Docker, 8 de seguridad Windows y 5 de integración PostgreSQL). Bloqueo de DLL unitaria por Control de aplicaciones de Windows no se deshabilitó. Compilación sin advertencias; lint/build Angular, contrato, formato y correspondencia de migración correctos. Vista móvil 390x844 sin desbordamiento (ancho de contenido 375px).

Incremento autenticación de integración: compilación Release sin advertencias/errores, OpenAPI/fixtures y compatibilidad correctos, modelo EF sin cambios pendientes. Ejecutadas 8 pruebas de integración PostgreSQL y 8 de seguridad: todas pasaron, sin omisiones. Las 16 pruebas unitarias anteriores no cambiaron ni se repitieron. Nueva migración agrega índice único de hashes de credenciales. No se implementaron todavía los endpoints de licencias ni la gestión de aplicaciones/credenciales.

Docker local actualizado con este incremento y migración aplicada sin errores. API/web/PostgreSQL saludables; sondas HTTPS live/ready 200 Healthy. La actualización conserva la cuenta administrativa del operador.

Incremento de gestión (12 de octubre de 2026): 11 pruebas de integración PostgreSQL y 8 de seguridad pasadas; aserciones adicionales de secretos/revocación verificadas en las 3 pruebas de gestión. Contrato y baseline compatibles. Angular lint/build correctos, módulo administrativo diferido e initial bundle 499.60 kB. Docker API/web actualizado; HTTPS live/ready, documento, bundles, CSP y PWA verificados. No se realizó login visual con los secretos reales del operador ni se crearon aplicaciones reales automáticamente.
