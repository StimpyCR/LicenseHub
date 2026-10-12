# ADR 0008: canje, idempotencia y códigos no recuperables

Se usan claves aleatorias de 256 bits, almacenamiento por hash y respuesta única de emisión. La idempotencia de emisión conserva metadatos sin los códigos; la de activación conserva el snapshot histórico. El consumidor debe consultar derechos actuales para autorizar.

El principal de activación es la aplicación autenticada, estable al rotar credenciales. La serialización por subjectId evita carreras entre diferentes licencias para el mismo negocio; el bloqueo de licencia y el índice único evitan canjes dobles entre sujetos distintos. Una licencia Suspended conserva su vínculo. Las expiradas se reemplazan bajo la misma transacción.

No se agrega una migración: el esquema inicial ya contiene las relaciones, índices únicos y restricciones necesarias. El contrato se revisó antes de endpoints. Los cursores de los nuevos listados se protegen con DataProtection persistente y se vinculan a aplicación/filtros.
