# ADR 0006: recuperación privada y mantenimiento de autenticación

Estado: recuperación operativa implementada y probada en bases aisladas. No recuperación autoservicio ni modificación automática de la cuenta real.

La pérdida del autenticador no puede dejar al operador sin procedimiento. Se añade CLI offline, con acceso privilegiado al host y secretos como autoridad explícita. UUID/email coincidentes y confirmación impiden confusiones operativas; no son factores de autenticación ni una autorización obtenida por email. Producción exige verificar identidad y doble control de operadores. No se expone endpoint remoto que omita MFA.

Se exige una nueva clave TOTP validada por Identity y contraseña nueva válida. Transacción PostgreSQL, bloqueo por usuario y SecurityStamp invalidan sesiones/desafíos anteriores. Auditoría identifica operador del sistema y motivo. Fallos revierten token/password; reintento con misma clave se rechaza. No cambia roles ni crea usuarios.

La limpieza conserva auditoría/Identity, elimina códigos MFA expirados y conserva desafíos/sesiones por retención explícita. Servicio probado idempotente; Hangfire lo registra por hora UTC solo cuando Jobs está habilitado. El esquema se provisiona con identidad separada, sin DDL automático desde el runtime. No habilitar sin prueba del almacenamiento persistente.
