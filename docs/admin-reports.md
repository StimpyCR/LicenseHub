# Informes administrativos

Cada informe exige applicationId y un permiso emparejado con esa aplicación: dashboard:read, audit:read o licenses:export. Un permiso de otra aplicación no autoriza el recurso solicitado. La auditoría global de autenticación no se expone a través de estos endpoints.

GET /api/v1/admin/dashboard devuelve conteos reales de clientes, licencias activas, pendientes vigentes y activas que vencen en siete días. Excluye licencias cuya expiración ya ocurrió aunque Hangfire aún no haya actualizado su estado. evaluatedAt es UTC. Los conteos se consultan por separado y pueden variar si existen escrituras concurrentes; no constituyen un cierre contable.

GET /api/v1/admin/audit-logs admite action exacta, página de 1 a 100 registros y cursor protegido vinculado a aplicación y filtro. Muestra responsable, recurso, motivo, fecha y traza. No incluye credenciales ni hashes. Los registros se ordenan por fecha descendente e identificador. La paginación no congela el conjunto: nuevas acciones pueden desplazar registros entre páginas; reiniciar la consulta permite ver los más recientes.

GET /api/v1/admin/licenses/export descarga CSV UTF-8 con identificación de licencia, estado efectivo, negocio, nombre del cliente, fechas UTC y condición vitalicia. Excluye claves, hashes y secretos. Un máximo de 10 000 filas se aplica antes de generar la descarga; excederlo devuelve 409 export_too_large, sin truncar silenciosamente. Cada exportación se audita. Las celdas se entrecomillan y se neutralizan prefijos de fórmulas y controles de línea para hojas de cálculo. El archivo contiene datos de clientes y debe custodiarse como información privada.

GET /api/v1/admin/customers admite query de hasta 120 caracteres. Busca coincidencias parciales literales en subjectId, nombre y correo, sin distinguir mayúsculas. Los signos % y _ no actúan como comodines. El cursor queda vinculado al filtro; modificarlo exige reiniciar desde la primera página. El listado de licencias conserva sus filtros de estado efectivo y cliente.

El panel incorpora informes por aplicación, filtro de auditoría, descarga y búsqueda de clientes. Los permisos se administran desde el panel de delegados; el servidor comprueba cada solicitud incluso si el navegador muestra controles para operaciones sin permiso.

## Estado de validación

72 pruebas Linux/Docker pasadas: 46 integración PostgreSQL, 9 seguridad y 17 unitarias. Incluyen permisos delegados emparejados, expiración efectiva, búsqueda literal, cursor vinculado al filtro, neutralización de fórmulas, rechazo de exportaciones excesivas y endpoints con sesión MFA. Quince respuestas HTTP reales validadas contra OpenAPI y compatibilidad de desarrollo comprobada.

Compilación Release sin advertencias ni errores; Angular lint/build correctos con aviso de presupuesto inicial 523,78 kB frente a 500 kB. Panel revisado con cuenta y base sintéticas: estadísticas reales, descarga y auditoría CSV, búsqueda por mayúsculas y recarga voluntaria de actualización PWA. Vista móvil 390×844 sin desbordamiento (ancho de contenido 375 px). Capturas en docs/screenshots/phase8-*.png. La licencia inicial del ensayo es un fixture SQL; no se presenta como una activación realizada desde el navegador.
