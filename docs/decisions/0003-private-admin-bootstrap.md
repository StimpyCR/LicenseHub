# ADR 0003: inicialización administrativa como comando privado

Estado: aceptado, implementado como primer incremento de fase 3.

## Decisión

SuperAdmin se crea mediante un comando del backend ejecutado por un operador con acceso a la configuración privada y a PostgreSQL. No existe ruta HTTP de registro/bootstrap. La preparación genera contraseña y clave TOTP aleatorias en archivos restringidos. El operador confirma un código antes de que la cuenta se confirme.

La capa Application define el caso de uso; Infrastructure coordina Identity y la transacción PostgreSQL; Api ofrece solamente el adaptador CLI. Un lock asesor transaccional global serializa la primera inicialización. La presencia de cualquier usuario cierra bootstrap. Identity conserva el hash de contraseña, rol SuperAdmin y MFA habilitado; auditoría se confirma en la misma transacción.

## Consecuencias

No se modifica OpenAPI porque no se incorpora ningún endpoint ni contrato SDK. El comando nunca emite sesión; los endpoints existentes en OpenAPI para login/MFA siguen pendientes. Las pruebas usan una base efímera independiente con sufijo `_tests` y verifican rollback, concurrencia, hash, MFA, rol y auditoría. CI necesita privilegio de creación de bases exclusivamente en su PostgreSQL aislado.

Se conserva el proveedor TOTP oficial de Identity. No se implementa criptografía de autenticación propia. Los cálculos TOTP del test solo generan un código del autenticador de prueba. El token estándar de Identity no está cifrado por DataProtection: acceso a base, cifrado de almacenamiento/backups y procedimiento de recuperación son responsabilidades operativas. La recuperación administrativa y prevención de replay en login se resolverán al implementar sesiones.

No se crea una cuenta real durante desarrollo, builds o pruebas. El operador debe escoger su identidad y confirmar su autenticador; automatizar eso sin su participación anularía el propósito del enrolamiento.

Actualización: el incremento [ADR 0004](0004-admin-mfa-sessions.md) implementa los endpoints administrativos de login/MFA, CSRF y sesiones que estaban pendientes al adoptar esta decisión.
