# Estado por fases

Estado actualizado el 13 de octubre de 2026. Los párrafos siguientes conservan el historial de verificación de cada incremento.

1. Arquitectura, modelo y contrato v1 diseñados. API versionada y pruebas HTTP reales; publicación de release y comprobación con generador del SDK corresponden a fase 9.
2. Base .NET/PostgreSQL/Angular/Docker implementada y verificada localmente.
3. Autenticación privada, MFA, recuperación, sesiones, CSRF, scopes y permisos por aplicación implementados. Aplicados a todos los endpoints de negocio disponibles. Validación productiva pendiente de fase 10.
4. Aplicaciones, credenciales, rotación/revocación, catálogo y delegados implementados y verificados con datos sintéticos.
5. Clientes, emisión/activación/consulta de licencias implementados y verificados.
6. Renovación, estados e historial inmutable implementados y verificados.
7. Pagos y notificaciones implementados y verificados. Transporte SMTP real probado localmente; proveedor operativo pendiente de configuración privada.
8. Dashboard, auditoría por aplicación, búsqueda de clientes, filtros, paginación y CSV implementados; 72 pruebas pasadas y revisión visual ejecutada. Ver docs/admin-reports.md.
9. Compatibilidad local verificada y contrato v1 congelado. SDK existente: 94 pruebas pasadas sin omisiones; fixtures persistentes por aplicación y HTTPS desde host. Ver docs/sdk-live-integration.md.
10. En curso: backup AES-GCM y restauración real aislada (39 tablas/DataProtection), integridad y mínimo privilegio verificados; plantilla productiva y proveedor externo opt-in preparados; Mailpit desarrollo añadido/verificado. Pendientes externos y evidencia en fase10-verification.md. No se habilitó correo ni desplegó a producción.

Verificación Docker (2026-10-11): cliente y motor 29.8.2 operativos. PostgreSQL 17 iniciado en contenedor aislado con contraseña aleatoria fuera del código. Prueba de integración: 1 pasada, 0 fallos y 0 omisiones. La prueba aplica la migración y verifica aislamiento por claves foráneas y unicidad de activación; no prueba aún endpoints ni activaciones concurrentes.






Verificación del incremento auth: 29 pruebas pasadas (16 unitarias Linux/Docker, 8 de seguridad Windows y 5 de integración PostgreSQL). Bloqueo de DLL unitaria por Control de aplicaciones de Windows no se deshabilitó. Compilación sin advertencias; lint/build Angular, contrato, formato y correspondencia de migración correctos. Vista móvil 390x844 sin desbordamiento (ancho de contenido 375px).

Incremento autenticación de integración: compilación Release sin advertencias/errores, OpenAPI/fixtures y compatibilidad correctos, modelo EF sin cambios pendientes. Ejecutadas 8 pruebas de integración PostgreSQL y 8 de seguridad: todas pasaron, sin omisiones. Las 16 pruebas unitarias anteriores no cambiaron ni se repitieron. Nueva migración agrega índice único de hashes de credenciales. No se implementaron todavía los endpoints de licencias ni la gestión de aplicaciones/credenciales.

Docker local actualizado con este incremento y migración aplicada sin errores. API/web/PostgreSQL saludables; sondas HTTPS live/ready 200 Healthy. La actualización conserva la cuenta administrativa del operador.

Incremento de gestión (12 de octubre de 2026): 11 pruebas de integración PostgreSQL y 8 de seguridad pasadas; aserciones adicionales de secretos/revocación verificadas en las 3 pruebas de gestión. Contrato y baseline compatibles. Angular lint/build correctos, módulo administrativo diferido e initial bundle 499.60 kB. Docker API/web actualizado; HTTPS live/ready, documento, bundles, CSP y PWA verificados. No se realizó login visual con los secretos reales del operador ni se crearon aplicaciones reales automáticamente.

Prioridades de fases 3–4: recuperación/rotación operativa privada de SuperAdmin, invalidación de sesiones/desafíos, auditoría y rollback MFA implementados. Preparador privado verificado con ACL restringida en directorio aislado y material retirado. Limpieza de registros expirados probada y tarea horaria registrada cuando Jobs está habilitado; Hangfire local provisionado, worker activo y ejecución de limpieza Succeeded comprobada; tarea horaria UTC persistida. Administración de otros usuarios implementada en el incremento siguiente. Prueba BOLA de endpoints de licencias futuros corresponde a fase 5. Catálogo/API/UI de credenciales completados a nivel de implementación. 15 pruebas de integración y 8 de seguridad pasadas; contrato y baseline compatibles. Angular lint/build pasan con aviso de presupuesto inicial 502.20 kB (límite 500 kB). No se cambia la cuenta real ni se avanza a licencias mientras se cierran/verifican estos bloques.


Cierre de prioridades 3–4 (12 de octubre): administración de delegados, permisos por aplicación, suspensión, enrolamiento y recuperación privados implementados y auditados. Contrato actualizado antes de endpoints. Compilación Release sin errores ni advertencias, formato correcto, 18 pruebas de integración PostgreSQL y 9 de seguridad pasadas sin omisiones; contrato y baseline compatibles. Las 16 unitarias previas no se repitieron porque el dominio no cambió. Lint/build Angular pasan con aviso de presupuesto inicial 509.60 kB frente a 500 kB. Revisión real del panel sobre datos sintéticos y móvil 390×844 sin desbordamiento; base temporal retirada. Falta la validación productiva de fase 10 y aplicar las políticas a licencias en fase 5. Siguiente bloque: clientes y generación/activación/consulta de licencias; SDK permanece separado.

Docker local actualizado con la migración de administradores; API, web y PostgreSQL saludables. Sondas HTTPS, bundles, CSP y recursos PWA verificados. La base principal conserva una cuenta administrativa; no se añadieron los usuarios sintéticos del ensayo.

Fase 5 implementada: clientes/contactos, emisión individual/lotes, claves de solo creación, activación única e idempotente, consulta de derechos y panel. Ver docs/license-operations.md. 51 pruebas Linux/Docker pasadas y seis respuestas HTTP validadas contra OpenAPI; lint/build Angular correcto con aviso de presupuesto 522.60 kB. Panel revisado en Docker aislado con datos sintéticos; mobile sin desbordamiento. Docker local actualizado y sondas HTTPS/bundles/CSP/PWA verificadas. Entorno sintético retirado. Siguiente: fase 6. GitHub se informa según resultado real del push.

Fase 6 implementada y verificada: renovación manual sin nueva clave, operationId permanente, suspensión/revocación/cancelación/reactivación, historial paginado y protección append-only. 58 pruebas Linux/Docker pasadas (32 integración, 9 seguridad, 17 unitarias), ocho respuestas HTTP compatibles; formato y baseline correctos. Angular lint/build pasan con aviso de presupuesto 522.61 kB. Panel aislado: renovación y replay sin duplicación, historial, suspensión/reactivación/revocación y restauración vitalicia explícita. La licencia activa inicial del ensayo era un fixture sintético, no una activación realizada en navegador. Cuenta real intacta y entorno temporal retirado. Ver docs/license-lifecycle.md.

Docker local fase 6 actualizado con ImmutableLicenseHistory; HTTPS live/ready, bundles, CSP y PWA correctos. Corrección CI df27a0b configura la ruta absoluta del workspace; exportación comprobada en Linux. No se afirma aún éxito del workflow remoto.

Fase 7 implementada y verificada localmente: pagos manuales transaccionales, confirmación/renovación única, outbox, Hangfire minutely, SMTP desacoplado y notificaciones InApp. 67 pruebas Linux/Docker pasadas (41 integración, 9 seguridad, 17 unitarias), trece respuestas HTTP compatibles. Angular lint/build correctos con aviso inicial 523.20 kB. Panel aislado revisado y móvil sin desbordamiento. SMTP real probado contra servidor de ensayo; proveedor operativo pendiente de configuración. Ver docs/payments-notifications.md. Siguiente: fase 8 (dashboard, auditoría, filtros y CSV).

GitHub Actions remoto verificado: CI de df27a0b y 4f51d36 finalizó success; la corrección de ruta absoluta de contract-results está comprobada.
Docker local actualizado con NotificationOutboxDeduplication y trabajos persistentes provisionados. HTTPS live/ready, frontend, bundles, CSP y PWA verificados. Cuenta principal conservada; entorno sintético y sus secretos retirados.

Fase 8: 72 pruebas Linux/Docker pasadas (46 integración, 9 seguridad, 17 unitarias), quince respuestas HTTP compatibles, formato C# y contrato correctos. Angular lint/build pasan con aviso de presupuesto 523,78 kB. Revisión aislada: dashboard, búsqueda, descarga CSV, auditoría y actualización PWA; móvil sin desbordamiento. GitHub Actions de fase 7 (78bbe60) finalizó success: https://github.com/StimpyCR/LicenseHub/actions/runs/38344689286.
Docker local fase 8 actualizado; HTTPS live/ready, bundles, CSP y PWA verificados. Cuenta principal conservada; entorno sintético y archivos privados retirados.

Fase 9 verificada: 74 pruebas LicenseHub pasadas, con repetición de los dos casos afectados; 94 pruebas SDK pasadas sin omisiones, una activación adicional de B, 83+30 comprobaciones HTTPS y 28 respuestas reales de los cuatro endpoints validadas. Contrato congelado SHA256 989e2e7b382489a32b727c3bb951a1cb313210a6a2cc308c29e4bc23f282f30d y copia SDK sincronizada. StockMini Integration Test UUID dde7150c-d78b-4c30-8258-2441bf4cc9d7, fixtures y credenciales privadas persistentes; cuenta del operador intacta. No se eliminó ni recreó la base principal. Endurecimiento JSON inválido y limitación de Windows documentados. OpenAPI HTTPS normal coincide con checksum; no publicación NuGet ni producción. CI fase 8 c961526 finalizó success.


Avance correo/seguridad local: Mailpit persistente, UI loopback, SMTP interno sin salida, 79 pruebas completas aprobadas incluyendo cuatro correos reales capturados. Trivy offline sobre imágenes/lockfiles; PostgreSQL conserva hallazgos críticos/altos pendientes de revisión autorizada. No se exportaron inventarios ni se habilitó Brevo; no se compró dominio. Fase 10 continúa abierta.


Candidata PostgreSQL evaluada sin tocar origen: scan offline 0 críticas/altas, restauración de 39 tablas y 79 pruebas pasadas. Ordenación glibc/musl diferente: decisión/migración pendiente, no reemplazo autorizado. Prueba Mailpit corregida para buzón preexistente sin limpiar capturas. Ver postgres-candidate.md.
