# Renovación y cambios administrativos de estado

POST /api/v1/admin/licenses/{id}/renew conserva la licencia, su canje y su clave original. Exige licenses:renew, sesión MFA, CSRF, If-Match, Idempotency-Key, motivo y operationId UUID permanente. Repetir la misma clave/cuerpo devuelve el resultado original antes de volver a comprobar ETag. Reutilizar operationId con otra clave produce 409 y no extiende vigencia. El índice único por aplicación conserva esta garantía independientemente de la retención de idempotencia.

Duration extiende desde max(ahora,ExpiresAt); Month/Year utilizan calendario UTC. Until debe superar ahora y el vencimiento anterior. Lifetime convierte en vitalicia. Pending sin canje y Cancelled no se renuevan. Una vitalicia activa no admite extensión; su reactivación puede renovar con Lifetime manteniendo su semántica. Suspended/Revoked requieren reactivate:true y licenses:reactivate adicional. Expired canjeada admite renovación normal; una clave vencida sin canje no.

La renovación registra vencimientos antes/después, conversión vitalicia, reactivación, administrador, motivo y operationId. Se serializa con la activación por aplicación/sujeto antes de bloquear la licencia. Si otra licencia ocupa la cuenta, no se desplaza: 409. Todos los cambios incrementan versión y entitlementVersion y se auditan. La migración ImmutableLicenseHistory protege activaciones y renovaciones con triggers que rechazan UPDATE/DELETE.

## Transiciones

| Comando | Estados permitidos | Resultado |
|---|---|---|
| suspend | Pending, Active | Suspended |
| revoke | Pending, Active, Suspended, Expired | Revoked |
| cancel | Cualquier estado salvo Cancelled | Cancelled terminal |
| reactivate | Suspended con vigencia restante | Active si canjeada; Pending si sin canje |
| renew | Active/Expired canjeadas; Suspended/Revoked con permiso explícito | Active |

Cada comando exige su capacidad licenses:<comando>, If-Match, motivo, CSRF e idempotencia. Reactivate no restaura Revoked ni Expired: requieren renovación explícita. El dominio y el servidor verifican las transiciones. Un estado efectivo vencido se calcula antes de decidir; no depende de una tarea programada.

## Derechos y propagación

status/features consultan el estado actual y devuelven cero derechos en Suspended/Revoked/Cancelled/Expired. No hay caché de autenticación ni de derechos en la API. El consumidor puede conservar un snapshot válido como máximo 30 segundos y nunca después del vencimiento. El servidor consumidor debe rechazar snapshots obsoletos y autorizar sus endpoints; no se promete revocación instantánea con caché.

## Panel e historial

Desde Clientes y licencias, consultar el listado y elegir Administrar esta licencia. Registrar motivo antes de renovar o cambiar estado. Autorizar reactivación explícitamente cuando corresponda. Los reintentos conservan UUID, operationId y ETag originales; Preparar otra renovación crea una intención nueva, que puede extender nuevamente. Actualizar el listado antes de editar una versión que cambió.

Los historiales `/activations` y `/renewals` exigen licenses:read sobre la aplicación real de la licencia. Paginación protegida y filtros ligados al recurso. No se revelan claves ni hashes. Las notificaciones por estos eventos se conectan en fase 7.
