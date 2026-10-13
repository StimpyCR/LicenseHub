# Entrega a LicenseHub.SDK

Contrato v1 revisado y congelado en contracts/releases/1.0.0, con checksum. Ver [contratos definitivos](sdk-contract-v1.md) e [integración HTTPS real](sdk-live-integration.md). El SDK permanece en su repositorio independiente; LicenseHub no incorpora su implementación.

Confirmados cuatro endpoints de integración, DTO camelCase, estados, UTC Z, Bearer opaco, scopes por aplicación, idempotencia y errores tipados. Copia/manifiesto del SDK sincronizados; baseline histórico conservado. El endurecimiento de JSON inválido está documentado.

Ejecutadas 94 pruebas del SDK sin omisiones, 74 de LicenseHub y comprobaciones HTTPS antes/después de transiciones. No se publica NuGet ni se despliega producción. El backend consumidor autentica usuario/negocio, determina subjectId y aplica autorización en sus endpoints; un replay histórico no decide Premium actual. Caché opcional máxima 30 segundos, fail closed.

Identificadores no sensibles en integration-fixtures.json; credenciales y claves solo en el almacén privado. La guía explica la ruta reproducible mediante el ejecutor oficial SDK, manteniendo Windows Application Control.
