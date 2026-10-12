# ADR 0005: credenciales opacas y permisos por aplicación

Estado: aceptada para la base de autenticación de fase 3. Emisión/rotación y endpoints de negocio todavía pendientes.

El contrato SDK usa un secreto Bearer opaco de 256 bits; no se requiere un servidor OAuth ni JWT para este flujo privado entre backends. Se almacenará SHA-256 del secreto, con índice único. Se comprueban credencial vigente, ausencia de revocación, aplicación activa y scopes reconocidos en PostgreSQL en cada solicitud. El esquema de integración no es el esquema predeterminado: cada endpoint seleccionará una política explícita para su scope. Los endpoints administrativos conservan Identity/MFA/CSRF.

Consultar almacenamiento evita conservar credenciales revocadas en caché a costa de una consulta por autenticación. Una solicitud en curso no se cancela retroactivamente; una operación sensible deberá validar otra vez dentro de su transacción. El SDK puede mantener derechos durante la ventana de caché documentada y no puede prometer revocación instantánea.

Los permisos administrativos se comprueban como tuplas usuario/aplicación/acción. Las listas agregadas de sesión solo sirven a la interfaz. Los futuros endpoints cargarán el recurso y autorizarán su aplicación real; un UUID externo o un applicationId enviado por el consumidor no cambia el tenant autenticado. Las pruebas actuales cubren los servicios y handlers, no los futuros endpoints BOLA de licencias.

No se implementa el SDK aquí. El contrato OpenAPI mantiene las rutas v1 y sus DTOs; se precisó la semántica de autenticación antes de implementar el esquema.
