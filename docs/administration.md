# Manual administrativo — flujos diseñados

El panel ya permite login/MFA y cerrar sesión. Inicializar SuperAdmin con la guía admin-bootstrap.md y consultar admin-authentication.md. Las siguientes operaciones de negocio son reglas previstas, todavía no pantallas implementadas.

SuperAdmin se inicializa mediante herramienta operacional sin registro público. En producción completa enrolamiento MFA antes de obtener permisos. Asigna permisos por aplicación; operador de StockMini no recibe acceso a otras aplicaciones. Los códigos de recuperación todavía no están implementados; conservar el autenticador según el procedimiento operativo.

Registrar aplicación y catálogo Premium; crear credencial mínima con vencimiento. Guardar secreto en el backend de la aplicación cuando se muestra una sola vez. Para rotar: crear nueva credencial, actualizar backend, comprobar integración y revocar anterior. Se pierde un secreto si la respuesta de creación no llega; revocar y volver a generar, no intentar recuperarlo.

Registrar cliente con subjectId estable que corresponda al negocio autenticado. Emitir licencia para esa aplicación, elegir inicio y duración o lifetime, y copiar código solo en creación. Dos minutos significa exactamente 120 segundos; meses/años siguen calendario UTC. Las claves canjeadas no se distribuyen a nuevos sujetos ni se reactivan por segundo canje.

Renovar licencia canjeada usando operationId único. Para pago manual: registrar Pending con intención de renovación, revisar evidencia y confirmar con permiso específico; la confirmación aplica la intención una sola vez. Rechazar y reembolsar siguen estados permitidos; reembolso no revoca Premium automáticamente. Renovación de Suspended/Revoked exige autorización explícita de reactivación y motivo.

Suspender/revocar con motivo y responsable; considerar la ventana de caché del SDK. Cancelled es terminal. Consultar historial y auditoría sin revelar hashes o códigos. Exportar CSV dentro de aplicación autorizada; neutralizar fórmulas y respetar información personal. Verificar entregas/reintentos de notificaciones cuando esté implementado el worker.

