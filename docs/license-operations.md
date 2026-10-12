# Clientes, emisión y activación v1

Los backends consumidores llaman POST /api/v1/licenses/activate y GET /api/v1/licenses/status o /features con su credencial Bearer. LicenseHub deriva la aplicación de la credencial. El subjectId es un identificador estable de negocio determinado por el backend después de autenticar al usuario; el SDK no sustituye esa autorización. No contiene customerId arbitrario enviado por el navegador.

## Emisión administrativa

POST /api/v1/admin/licenses requiere sesión MFA, CSRF, Idempotency-Key y licenses:issue en la aplicación. Cantidad 1–100; preasignar un cliente admite cantidad 1. Funcionalidades únicas del catálogo activo. Las claves LH1_ contienen 256 bits aleatorios y se guardan como SHA-256. No aparecen en auditoría ni registros de idempotencia. La respuesta inicial contiene los códigos; el replay devuelve 200, codesRecoverable false y licenseKey null. Si se pierde la primera respuesta, los códigos no pueden recuperarse: emitir otro lote mediante una nueva intención administrativa y gestionar el anterior según su estado.

OnIssue inicia el reloj al emitir, pero mantiene Pending hasta el canje. OnActivation inicia el reloj dentro del canje. Duration usa minutos/horas/días y meses/años calendario UTC; Until exige OnIssue, fecha futura UTC y excluye duration. Lifetime no admite fecha ni duración. Overflow se rechaza. Un reloj de dos minutos realmente vence al cumplirse esos dos minutos.

## Activación y consultas

La transacción serializa reintentos por aplicación/operación/UUID y canjes por aplicación/subjectId mediante advisory locks PostgreSQL; bloquea la fila de licencia y conserva unicidad de activación en la base. Una clave no puede canjearse dos veces, aun en el mismo negocio. Otro UUID para una clave canjeada da 409. Un cliente preasignado debe coincidir. Se permite una licencia Active/Suspended por cliente; al reemplazar una vencida se cambia su estado a Expired antes de vincular la nueva. La suspensión bloquea reemplazos accidentales.

El replay devuelve el resultado histórico original y nunca debe usarse como autorización actual. Consultar status/features antes de autorizar endpoints Premium en el backend consumidor. Los distintos dispositivos de una cuenta consultan el mismo subjectId; no vuelven a activar. Una rotación de credencial de la misma aplicación conserva la identidad de idempotencia. Una aplicación distinta recibe 404 al presentar una clave ajena.

Sin licencia activada: 200, license null, effectiveState None, cero derechos. Las consultas priorizan la licencia Active/Suspended; después, el historial activado más reciente. Calculan vencimiento al leer, sin depender de un job. Features desactivadas no conceden permisos. Las respuestas no permiten caché HTTP; el consumidor puede almacenar un snapshot hasta cacheUntil, como máximo 30 segundos y nunca después de expiresAt. No utilizar respuestas obsoletas tras cacheUntil ni prometer revocación instantánea.

## Clientes y permisos

customers:read/manage y licenses:read/issue se conceden por aplicación. Los sujetos y aplicaciones del cliente son inmutables; editar contacto exige If-Match, CSRF e idempotencia. Los listados tienen paginación con cursor protegido por DataProtection y ligado a la aplicación y filtros. Los errores ajenos son 404. La API usa CancellationToken en consultas y transacciones.

## Validación ejecutada

51 pruebas Linux/Docker: 26 integración PostgreSQL, 9 seguridad y 16 unitarias; ninguna omitida. Windows bloqueó la DLL API mediante Control de aplicaciones y se conservó esa protección. Seis respuestas HTTP de activación, snapshots, cliente y emisión/replay contrastadas con JSON Schema de OpenAPI; los códigos sintéticos se eliminan del archivo de evidencia. CI ejecuta esta validación y la compatibilidad del baseline SDK.

Panel Docker aislado con cuenta sintética: cliente creado/editado, licencia preasignada, reintento sin códigos y lote de dos vitalicias. Vista móvil 390×844, contenido 375px sin desbordamiento. Capturas sin claves en screenshots/phase5-licenses-*.png. Angular lint/build correctos con aviso de presupuesto inicial 522.60 kB frente a 500 kB. No se creó una aplicación o licencia real automáticamente para el operador.

Los avisos consultables, outbox y correo corresponden a fase 7. El SDK se desarrolla fuera de este repositorio; estas pruebas verifican la API, no un paquete SDK publicado. Renovaciones y cambios administrativos de estado corresponden a fase 6.
