# Manual administrativo

El panel permite login/MFA, sesión privada y gestión de aplicaciones, credenciales, catálogo, clientes, licencias, renovaciones, pagos, notificaciones e informes. Inicializar SuperAdmin con admin-bootstrap.md y consultar admin-authentication.md. Las operaciones descritas están implementadas; el servidor valida permisos por aplicación y estados, además del formulario.

SuperAdmin se inicializa mediante herramienta operacional sin registro público. Completa enrolamiento MFA privado antes de obtener permisos en cualquier ambiente. Asigna permisos por aplicación; operador de StockMini no recibe acceso a otras aplicaciones. Los códigos de recuperación todavía no están implementados; conservar el autenticador según el procedimiento operativo.

Registrar aplicación y catálogo Premium; crear credencial mínima con vencimiento. Guardar secreto en el backend de la aplicación cuando se muestra una sola vez. Para rotar: crear nueva credencial, actualizar backend, comprobar integración y revocar anterior. Se pierde un secreto si la respuesta de creación no llega; revocar y volver a generar, no intentar recuperarlo.

Registrar cliente con subjectId estable que corresponda al negocio autenticado. Emitir licencia para esa aplicación, elegir inicio y duración o lifetime, y copiar código solo en creación. Dos minutos significa exactamente 120 segundos; meses/años siguen calendario UTC. Las claves canjeadas no se distribuyen a nuevos sujetos ni se reactivan por segundo canje.

Renovar licencia canjeada usando operationId único. Para pago manual: registrar Pending con intención de renovación, revisar evidencia y confirmar con permiso específico; la confirmación aplica la intención una sola vez. Rechazar y reembolsar siguen estados permitidos; reembolso no revoca Premium automáticamente. Renovación de Suspended/Revoked exige autorización explícita de reactivación y motivo.

Suspender/revocar con motivo y responsable; considerar la ventana de caché del SDK. Cancelled es terminal. Consultar historial y auditoría sin revelar hashes o códigos. Exportar CSV dentro de aplicación autorizada; neutralizar fórmulas y respetar información personal. Verificar entregas/reintentos de notificaciones en el worker persistente, sin confundir captura local con entrega operativa.



El dashboard y la auditoría requieren escoger aplicación autorizada. Buscar clientes por texto y usar paginación; los cursores están vinculados a aplicación/filtros. CSV está limitado a 10000 filas y neutraliza fórmulas; no exporta claves ni secretos. Guardar códigos/credenciales durante creación antes de recargar la PWA; la recarga de actualización es voluntaria.

La recuperación del autenticador es una CLI privada auditada (admin-recovery.md), no un bypass por pantalla. Gmail sigue pendiente de cuenta/remitente verificados: Failed/Skipped no son envíos exitosos. Nunca utilizar datos del operador para pruebas de correo. Consultar smtp-configuration.md y backup-restore.md antes de habilitar servicios operativos.
