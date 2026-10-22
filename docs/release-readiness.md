# Estado de preparación para producción

15 de octubre de 2026. Las fases 1–9 están verificadas; Fase 10 sigue abierta. Este documento reúne los bloqueos actuales para evitar confundir avances históricos con un despliegue aprobado. No se creó una cuenta de alojamiento ni se contrató servicio alguno.

Actualización posterior: el operador confirmó continuar solo en Docker local y Windows; alojamiento/respaldo externo y otros dispositivos quedan pospuestos. Se completó el [paquete local de aceptación](phase10-local-acceptance.md): artefactos actuales, 11 comprobaciones Production sintéticas, 15 de Windows Edge, arreglo responsive, frontend sin hallazgos reportados, copia cifrada restaurada y 23 pruebas de herramientas. No convierte los pendientes externos/manuales en aprobados.

Actualización del **22 de octubre**: [nuevo informe local](local-update-2026-10-22.md), con parche Vite 8.3.5 y lockfiles sin hallazgos, imágenes API/web reevaluadas, suite 90/90, 11 comprobaciones Production y 15 Windows repetidas, credenciales de pruebas renovadas y actualización local con reversión. API: 21 medias/5 bajas sin versión corregida indicada; frontend: cero hallazgos. CI de esta revisión debe comprobarse separadamente; la fila histórica de CI no acredita un commit posterior. PWA instalada sigue pendiente por fallo del control interactivo, aunque el operador autorizó Edge.

| Elemento | Evidencia actual | Condición para cerrar |
| --- | --- | --- |
| Backend, seguridad y correo local | 90/90 pruebas .NET, sin omisiones; PostgreSQL/Mailpit aislados | Repetir verificaciones sobre el artefacto elegido para desplegar |
| CI de la candidata | 1e155ed: success, run 38768073618 | Mantener CI verde en la revisión final |
| SDK/API v1 | Contrato congelado y compatible; 94 pruebas SDK reales en Fase 9 | No cambiar el contrato sin evolución explícita |
| Imagen PostgreSQL y política de texto | Migración local autorizada y verificada; 39 tablas idénticas antes del corte, 90 pruebas posteriores, cero hallazgos reportados; cambios de orden/54 mapeos aceptados | Mantener evidencia reciente y repetir validaciones para el destino productivo |
| Control de escaneo y copias | Informes ligados al artefacto; 14 pruebas de escaneo + 9 de copia/retención; API sin críticas/altas, frontend final y PostgreSQL sin hallazgos reportados | Reevaluar hallazgos medios/bajos API y repetir escaneo antes de salida |
| Rol runtime | Local usa rol mínimo verificado; readiness productiva del código rechaza privilegios administrativos directos/alcanzables | Provisionar y verificar también en producción; revisar el artefacto API elegido |
| Alojamiento y dominio/TLS | Usuario aún no eligió proveedor; plantilla endurecida preparada | Elegir destino, permisos, host real y certificados; verificar HTTPS y readiness allí |
| Backups | AES-GCM, manipulación/clave incorrecta, copia verificada y restauración real; retención propuesta sin borrado | Elegir custodia independiente, destino externo, frecuencia, retención, alertas y ensayar recuperación en destino |
| PWA en dispositivos | 15 comprobaciones Windows Edge headless: UI/MFA, cuatro viewports, offline/reconexión, logout y persistencia cifrada | Instalación/actualización manual Windows y otros SO reales pendientes; ver pwa-verification.md |
| Correo externo | Mailpit operativo para pruebas | Brevo/remitente autenticado pospuestos por decisión del usuario; no activar ni contratar dominio ahora |

Las 14 pruebas Python son adicionales a las 90 .NET, no una nueva ejecución de estas últimas ni del SDK. El control de escaneo no es una autorización de despliegue ni sustituye compatibilidad, revisión operativa, restauración o pentest. Ningún inventario privado se publica en CI.

## Secuencia para completar la salida

1. Conservar el [informe de migración local y reversión](local-migration-2026-10-15.md). La aceptación del orden/Unicode y el corte local ya se ejecutaron; no equivalen a autorización productiva.
2. Elegir alojamiento y almacenamiento de backups; preparar configuración privada real sobre las plantillas existentes. No inventar credenciales, dominio ni una política de retención acordada.
3. Ensayar en instalación nueva: migraciones con propietario separado, runtime mínimo, restauración, TLS/DP y monitorización/readiness. Medir recuperación; las mediciones locales no son un SLA.
4. Ejecutar las pruebas PWA físicas y documentarlas. Solicitar autorización final sobre imagen/configuración/migración concretas; no desplegar automáticamente.

No hay un cambio de producción autorizado. La migración local terminó; el procedimiento general de corte/retorno está en postgres-migration-runbook.md y el ensayo ejecutado en local-migration-2026-10-15.md. Se conserva la base anterior detenida: después de permitir escrituras en el destino no se puede volver sin más al volumen antiguo.

## Criterios de alojamiento económico

Hace falta almacenamiento persistente para PostgreSQL y Data Protection, una instancia capaz de mantener Hangfire activo, conectividad segura a DB, backups recuperables, secretos privados y monitorización. Un sistema de archivos efímero no sirve como única ubicación de esas claves o datos. El tamaño se decide con mediciones de memoria/carga y crecimiento, no con una promesa de capacidad sin ensayo.

Para demostraciones gratuitas se pueden evaluar plataformas con suspensión; el retraso de trabajos/notificaciones debe ser aceptado explícitamente. No equivalen a operación continua: [Render documenta suspensión de servicios Free y expiración de PostgreSQL Free a 30 días](https://render.com/docs/free). Su [FAQ explica el filesystem efímero por defecto](https://render.com/docs/faq).

Para un piloto continuo, una opción a evaluar es un VPS económico con almacenamiento persistente: permite usar Compose y controlar roles, a cambio de asumir parches, firewall, monitorización y recuperación. [Hetzner publica opciones y almacenamiento Cloud](https://www.hetzner.com/cloud/cost-optimized/) y su [documentación de backups/snapshots](https://docs.hetzner.com/cloud/servers/backups-snapshots/overview/). No es una selección ni un presupuesto: región, IPv4, impuestos, backups, almacenamiento externo y transferencia pueden alterar el costo final. Los snapshots del servidor no sustituyen el backup lógico cifrado y la prueba de recuperación independiente.

No se contrató ni recomendó un plan concreto como suficiente para producción. Cuando el usuario elija destino/presupuesto se verificará su oferta vigente y se ajustará el despliegue; no se habilitan exports de inventario para ello.

## Protocolo PWA pendiente en dispositivos físicos

Registrar por Windows, Android, macOS, iOS y Linux: modelo/dispositivo, SO, navegador/versión, revisión desplegada, fecha, resultado y limitaciones. Usar HTTPS válido y cuentas/fixtures dedicadas; no fotografiar claves de licencia, secretos o QR de MFA.

En cada entorno comprobar: carga inicial, instalación cuando el navegador la permita, apertura instalada, login/MFA, navegación responsive, expiración de sesión/logout, actualización voluntaria conservando trabajo sin guardar y recuperación tras cerrar/reabrir. Registrar como no soportada/pendiente una función que el navegador no ofrezca; no simular un aprobado.

Con red desconectada, comprobar que la interfaz muestra indisponibilidad y no autoriza operaciones ni presenta como actuales derechos cacheados. Reconectar y verificar sesión/estado del servidor. Las respuestas /api, /health y datos administrativos no deben servirse desde la caché PWA. Las pruebas de recursos o emulación responsive no acreditan estos resultados físicos. El sistema de licencias requiere conexión conforme al contrato; no ofrece activación Premium offline.
