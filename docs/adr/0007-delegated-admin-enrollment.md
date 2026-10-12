# ADR 0007: administradores delegados y enrolamiento privado

La administración permanece privada y no ofrece registro público. SuperAdmin crea cuentas pendientes y concede capacidades por aplicación; no existe asignación remota de SuperAdmin.

Contraseña y MFA se configuran mediante un comando operativo privado, con comprobación TOTP y transacción. La recuperación conserva roles y permisos, y rechaza cuentas suspendidas. El acceso al host es la autoridad operativa; en producción debe verificarse la identidad del titular según la guía de seguridad.

La modificación de accesos usa concurrencia optimista y revoca sesiones. Las comprobaciones de autorización consultan el estado persistente para impedir que una sesión anterior conserve derechos retirados. El contrato OpenAPI se diseñó antes de los endpoints y el SDK continúa en su propio repositorio.
