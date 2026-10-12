# ADR 0004: MFA obligatorio y sesiones administrativas persistentes

Estado: implementado como segundo incremento de fase 3.

## Decisión

La contraseña correcta solamente produce un desafío temporal. MFA es obligatorio también en desarrollo. El TOTP se valida con Identity y el acceso se emite después de consumir el desafío y registrar una sesión en PostgreSQL. Cada solicitud contrasta cookie, registro de sesión, SecurityStamp, MFA, lockout y roles actuales. Logout revoca el registro para que una cookie copiada deje de servir.

Se usan cookies de esquemas separados para desafío y sesión; el desafío no participa en la autorización administrativa. CSRF explícito protege login/MFA/logout. Después de MFA cambia la identidad del token antiforgery, por lo que el cliente obtiene otro token antes de mutaciones. El frontend solo guarda la vista y el CSRF en memoria. Una respuesta de red incierta al cerrar sesión no se muestra como revocación confirmada.

Un bloqueo PostgreSQL por usuario serializa el contador de fallos, consumo de desafío, registro de código utilizado y logout. No basta consumir un desafío: dos desafíos diferentes podrían aceptar el mismo TOTP; por eso se conserva un hash del código por usuario durante tres minutos. La clave estándar Identity se mantiene fuera del código y requiere protección del almacenamiento/backups.

## Ventanas y consecuencias

Desafío: cinco minutos. Sesión: 30 minutos sin renovación. Replay: tres minutos. El proveedor Identity admite los pasos -2 a +2, de 30 segundos cada uno: ver [fuente v10.0.12](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Identity/Extensions.Core/src/AuthenticatorTokenProvider.cs). Esa ventana es limitada, aunque más amplia que aceptar exclusivamente el paso actual; sincronizar relojes y mantener lockout/rate limiting.

El diseño añade coste de lectura de PostgreSQL a cada solicitud administrativa y falla cerrado si no puede validar la sesión. No se usa caché de autorización administrativa. Las capacidades agregadas no autorizan combinaciones arbitrarias de aplicaciones/permisos. La autenticación de integración tendrá su propio esquema y se implementará por separado.

La limpieza de registros expirados, recuperación del autenticador, gestión de otros administradores y permisos de endpoints de negocio quedan pendientes. Ninguno de esos endpoints se habilita con respuestas simuladas. Se conserva contrato SDK v1; se elimina del draft administrativo la excepción Development sin MFA antes de implementar endpoints.
