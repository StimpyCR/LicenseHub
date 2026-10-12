# Administración de aplicaciones y credenciales

Este incremento de fase 4 implementa listado paginado, creación y edición de aplicaciones, activación/desactivación, listado de credenciales, emisión y revocación. El formulario Angular permite registrar y editar aplicaciones al SuperAdmin; los administradores delegados solo ven las aplicaciones con `applications:read`. La interfaz de gestión de credenciales y el catálogo Premium siguen pendientes.

Todas las rutas requieren sesión Identity con MFA; las mutaciones requieren CSRF e Idempotency-Key UUID no vacío. SuperAdmin crea/edita aplicaciones. Las credenciales exigen `credentials:read` o `credentials:manage` sobre su aplicación real. Recursos ajenos devuelven 404. `slug` es único e inmutable; nombre admite entre 1 y 120 caracteres y no puede ser solo espacios.

Los PUT de aplicaciones y las revocaciones exigen If-Match con el etag publicado. Ausencia responde 428; versión anterior o incorrecta, 412. EF controla Version para impedir escrituras concurrentes; una escritura exitosa incrementa la versión. Desactivar una aplicación impide autenticar sus credenciales; no revoca físicamente todas las credenciales, por lo que reactivarla permite reutilizar las que sigan vigentes y sin revocar.

Las mutaciones se ejecutan en transacción junto con auditoría e idempotencia. Un lock asesor PostgreSQL serializa reintentos de la misma operación/administrador/key. Mismo cuerpo y key devuelve el resultado original; otro cuerpo devuelve 409 idempotency_conflict. El registro se conserva al menos 30 días, sin tarea de borrado implementada todavía. El formulario conserva la key ante fallo para reintentar los mismos datos. No hay borrado de aplicaciones ni del historial.

Emisión devuelve 201 con `credential` y `secret`. El secreto contiene 32 bytes CSPRNG base64url; solo se guarda SHA-256. No aparece en auditoría, listados ni respuesta persistida de idempotencia. Si se pierde la respuesta, repetir la misma operación devuelve 409 secret_not_recoverable y no crea otra credencial. Consultar el listado, revocar la credencial correspondiente y emitir otra con key nueva. No se pueden recuperar secretos.

Expiración de credenciales debe ser UTC, futura y no superior a 365 días. Scopes permitidos: licenses:activate, licenses:read, warnings:read, sin repetidos. Rotación: emitir otra credencial, instalarla en el backend consumidor, comprobarla y revocar la anterior con su etag y un motivo no vacío de hasta 1000 caracteres. No distribuir secretos en Angular ni apps instalables.

Listados usan pageSize 1–100, predeterminado 25, y nextCursor. El cursor de este incremento codifica un desplazamiento decimal validado; no es una autorización ni una instantánea. Altas concurrentes pueden cambiar páginas; recargar el listado para confirmar estado. Las aplicaciones se ordenan por slug, las credenciales por CreatedAt/Id.

Pruebas sobre PostgreSQL aislado verifican creación única con reintentos concurrentes, conflicto de payload, versiones antiguas, emisión sin secreto persistido/replay, revocación y rechazo de acceso ajeno. Una prueba HTTP verifica sesión/MFA/CSRF, campos obligatorios, idempotencia, ETag y If-Match. No se crearon aplicaciones o credenciales reales del operador automáticamente.
