# Integración real desde el host

Se reutiliza licensehub-local y su PostgreSQL persistente. URL: https://localhost:8443/. 8444 era temporal y fue retirado. El SDK no accede a PostgreSQL. TLS usa validación normal y certificado local confiable, sin callbacks permisivos ni SkipCertificateCheck.

## Identificadores y almacén

StockMini Integration Test: dde7150c-d78b-4c30-8258-2441bf4cc9d7. Credencial principal: ec24251e-6585-4b19-a798-9c97b7915d47. Segunda aplicación: 5bc97582-c269-4e74-b5f5-afbdc628de4d. Identificadores de nueve clientes/licencias y subjectId en integration-fixtures.json, sin secretos.

Material privado: .secrets/sdk-integration/state.json, excluido de Git y restringido por ACL al usuario. Contiene credenciales y códigos emitidos: no copiar a logs, chat, CI o clientes instalados. PostgreSQL conserva hashes. Credenciales válidas hasta 2026-10-20T05:16:17.3889531Z; consultar credentialExpiresAt del manifiesto. Es un almacén de desarrollo protegido por permisos, no un vault productivo cifrado.

La principal solo tiene licenses:activate, licenses:read y warnings:read de StockMini Integration Test. Hay una de lectura, otra revocada y otra de B para pruebas negativas. Ninguna tiene administración. Correo deshabilitado para ambas aplicaciones de ensayo; clientes sin correo, InApp activo.

## Ejecución comprobada con las fixtures existentes

PowerShell 7, desde LicenseHub:

```powershell
./tools/run-sdk-integration.ps1 -Stage Sdk -NoBuild
```

Ejecuta la solución SDK vecina por HTTPS, incluyendo los grupos reales. El wrapper importa secretos solo al proceso y restaura el entorno. -SdkDirectory admite otra ruta absoluta. Compilar la solución SDK primero si sus fuentes cambiaron; NoBuild usa los binarios ya comprobados.

Repetir solo activación/replay de la fixture exclusiva de B:

```powershell
./tools/run-sdk-integration.ps1 -Stage Sdk -IsolationProbe -NoBuild
```

Los códigos están canjeados y se conserva el UUID: repetir confirma replay. Mantener estados expirados/revocados y no sustituir indiscriminadamente sus claves.

## Variables requeridas

| Variable | Uso |
|---|---|
| LicenseHub__BaseUrl | https://localhost:8443/ |
| LicenseHub__ApplicationId | UUID real de StockMini Integration Test |
| LicenseHub__ClientSecret | Bearer leído del almacén; nunca literal versionado |
| LICENSEHUB_LIVE_TESTS | 1 |
| LICENSEHUB_ALLOW_TEST_ACTIVATION | 1, únicamente fixtures |
| LICENSEHUB_TEST_SUBJECT / KEY / OPERATION / FEATURE | Negocio, clave privada, UUID persistido, advanced_reports |
| LICENSEHUB_TEST_REVOKED_SUBJECT / EXPIRED_SUBJECT | Estados denegados |
| LICENSEHUB_TEST_REVOKED_CREDENTIAL | Token privado realmente revocado |
| LICENSEHUB_TEST_OTHER_APP / OTHER_CREDENTIAL / OTHER_SUBJECT / OTHER_FEATURE | B y negocio exclusivo |
| LICENSEHUB_TEST_RENEWED_SUBJECT / RENEWED_EXPIRY / RENEWED_VERSION | Resultado administrativo real |
| LICENSEHUB_TEST_BEFORE_RENEWAL_EXPIRY / BEFORE_RENEWAL_VERSION | Evidencia previa, UTC con precisión conservada |

El wrapper carga todos los valores desde el archivo privado y no los persiste globalmente. Un backend consumidor debe usar el proveedor SDK con entorno o secret manager; nunca Angular.

## Preparación y transiciones

```powershell
./tools/prepare-sdk-integration.ps1 -ActorId '<SuperAdminUuid>' -SkipBuild
```

Con el archivo existente conserva aplicaciones, credenciales y licencias. La CLI privada solo funciona en Development/Testing; verifica rol/MFA y usa los mismos servicios que la API. No lee/modifica contraseña, TOTP o sesiones ni migra/elimina/recrea la base. Audita operaciones. -Transition -SkipBuild confirma transiciones conservando ETag/UUID y no duplica la renovación.

Un ciclo nuevo requiere otro StateFile dentro de .secrets/sdk-integration, para conservar el anterior y separar negocios. Activar sus fixtures y preparar estados antes de ejecutar pruebas finales. El ejecutable adicional tools/LicenseHub.SdkCompatibility ofrece Before/After con opt-in y verificó expiración natural desde el host. Windows bloqueó una dependencia recompilada de esa herramienta; se mantuvo la protección y se completó mediante el ejecutor oficial SDK. En este host, usar Stage Sdk -NoBuild; no se promete autorización de la herramienta auxiliar por la política del equipo.

## Evidencia ejecutada

- LicenseHub: 74 pasadas (48 integración PostgreSQL, 9 seguridad, 17 unitarias); los dos casos afectados se repiten tras ampliar la fixture B.
- SDK: 94 pasadas (37 net8, 37 net10, 2 contrato, 18 integración/pipeline), cero fallos y omisiones. Una prueba adicional activó B.
- Comprobador HTTPS: 83 verificaciones iniciales y 30 posteriores; expiración natural, lifetime, renovación única, revocación, suspensión, cancelación, errores y mismo negocio en dos aplicaciones.
- 28 respuestas HTTPS de los cuatro endpoints validadas; otras 15 respuestas HTTP y 4 fixtures wire SDK también validadas.
- Claves/credenciales ausentes de logs, TRX y evidencia pública inspeccionados.

Compatibilidad local de fase 9; no certifica StockMini productivo, NuGet publicado, SMTP operativo, dispositivos ni seguridad/respaldo de fase 10. Retry 429/503 se prueba en el pipeline SDK; no se detuvo ni saturó el servicio principal para simularlo.
