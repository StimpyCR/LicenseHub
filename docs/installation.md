# Instalación y ejecución local

Requisitos: .NET SDK 10.0.400 (global.json), Node 24.15.0 compatible con Angular 22, Python 3.12 para contratos, PostgreSQL 17 y Docker Compose. NuGet/npm están bloqueados en packages.lock.json/package-lock.json. No ejecutar upgrades automáticos.

## Arranque asistido (PowerShell 7)

Desde la raíz LicenseHub:

```powershell
./tools/start-local.ps1
./tools/verify-local.ps1
```

El arranque genera contraseña aleatoria solo si no existe, exporta certificado HTTPS y guarda archivos en .secrets/compose, excluidos del repositorio/contexto Docker. No imprime secretos. Valida Compose, construye imágenes, espera PostgreSQL, aplica SQL idempotente deteniéndose ante errores e inicia API/web. No cambiar la contraseña mientras se conserve el volumen PostgreSQL. Usar -SkipBuild para repetir sin recompilar.

Abrir https://localhost:8443. Para confiar en el certificado del navegador: dotnet dev-certs https --trust. verify-local comprueba sondas, Angular, bundles, CSP y PWA; valida normalmente el certificado TLS confiable local; no demuestra DNS/TLS productivo ni instalación PWA en dispositivos.

El script establece LICENSEHUB_DB_PASSWORD_FILE, LICENSEHUB_DB_CONNECTION_FILE, LICENSEHUB_TLS_CERTIFICATE_FILE y LICENSEHUB_TLS_KEY_FILE. La conexión interna usa Host=postgres;Database=licensehub;Username=licensehub_local y la contraseña privada. Certificado/clave son PEM para localhost. No hay contraseñas predeterminadas.

Proxy con IP privada explícita y único autorizado para reenviar HTTPS. Si la subred entra en conflicto, modificarla junto con TrustedProxies__0. API sin puerto público; PostgreSQL y web solo en loopback. Desde la misma terminal, docker compose -f docker/compose.yaml stop conserva volúmenes. Compose es exclusivamente de desarrollo. Runtime API UID 1654 y nginx UID 101; sondas live/ready verificadas con 200 Healthy.

## Backend y migraciones

```powershell
dotnet restore backend/LicenseHub.slnx --locked-mode
dotnet build backend/LicenseHub.slnx -c Release --no-restore
dotnet test backend/tests/LicenseHub.SecurityTests -c Release --no-build --no-restore
```

Configurar ConnectionStrings__LicenseHub mediante entorno/secret manager, o ConnectionStrings__LicenseHubFile apuntando a archivo privado. No poner contraseñas en comandos guardados, historial o documentación. Para generar scripts sin conectar: Host=127.0.0.1;Database=licensehub_design;Username=design_only, sin credenciales válidas.

Las migraciones incluyen Identity, modelo de negocio, sesiones/MFA e índice único de hashes. No se aplican al arrancar la API. Revisar docker/migrations.sql y ejecutarlo con identidad de migración separada del runtime. SQL idempotente requiere privilegios para tablas y trigger/función de auditoría. Verificado en PostgreSQL 17 real.

```text
dotnet tool restore
dotnet ef database update --project backend/src/LicenseHub.Infrastructure --startup-project backend/src/LicenseHub.Infrastructure
dotnet run --project backend/src/LicenseHub.Api --launch-profile https
```

Backend usa https://localhost:7276 y certificado confiable. /health/live comprueba proceso; /health/ready exige conexión y migraciones aplicadas. Solo sondas aceptan HTTP. Otras rutas lo rechazan antes de aceptar secretos. /openapi/v1.yaml se sirve solo en Development. No hay registro público.

Bootstrap privado, login/MFA, CSRF, sesiones, esquema Bearer, permisos por aplicación y gestión de aplicaciones/credenciales están implementados. Licencias siguen pendientes. El arranque local provisiona Hangfire mediante --provision-jobs y habilita la limpieza horaria; runtime sin DDL automático. Producción requiere provisionamiento separado. Notificaciones y outbox siguen pendientes de fase 7. El runtime requiere permisos mínimos; el usuario amplio de Compose no es apropiado para producción.

## Frontend

```powershell
cd frontend/licensehub-web
npm ci
npm run lint
npm run build
$env:NODE_EXTRA_CA_CERTS = (Resolve-Path ../../.secrets/compose/localhost.pem).Path
npm start
```

Arrancar Compose antes. Angular usa HTTPS con archivos TLS privados de Compose; proxy /api y /health hacia https://localhost:8443 con secure=true. No desactivar validación TLS. Cambiar backend exige ajustar target y confianza conjuntamente. Certificados nunca se incluyen en bundles. Proxy verificado con CSRF 200 en servidor temporal 4201, detenido después.

Panel con tema claro/oscuro, responsive, disponibilidad real, login/MFA y formulario de aplicaciones. PWA de producción cachea recursos estáticos; API/health/OpenAPI fuera de navegación offline. Instalación iOS/Android pendiente.

## Contratos y pruebas

En Python aislado:

```text
python -m pip install -r contracts/requirements-validation.txt
python contracts/validate_contract.py
python contracts/check_compatibility.py
```

Crear base exclusiva con nombre terminado en _tests, configurar LICENSEHUB_TEST_CONNECTION fuera del código y ejecutar dotnet test backend/tests/LicenseHub.IntegrationTests -c Release. Exigen PostgreSQL real, fallan si falta conexión y aplican migraciones. Fixtures usan transacciones o bases aisladas eliminadas al finalizar. Cubren FK/unicidad, autenticación y administración; veinte canjes simultáneos del futuro endpoint siguen pendientes.

Si Windows bloquea la DLL unitaria mediante Control de aplicaciones, mantener protección y verificar en Linux:

```text
docker build -f docker/Dockerfile.tests -t licensehub-tests .
docker run --rm licensehub-tests backend/tests/LicenseHub.UnitTests
```

El contenedor restaura con lock y compila sin modificar el host. Se verificaron así 16 pruebas unitarias. Integración y seguridad se ejecutan en Windows con PostgreSQL real. Ver estado actual en README y docs/progress.md.


## Verificación operacional

En Windows, si Control de aplicaciones bloquea DLL/ejecución de pruebas, conservar esa protección y usar `./tools/test-backend-docker.ps1` con el PostgreSQL de pruebas existente (`licensehub-integration-pg` y archivo privado `.secrets/postgres-test-password`). La herramienta nunca apunta al DB principal. No imprime contraseña ni la pasa como valor en argumentos. Ver [backup/restauración](backup-restore.md), [SMTP configurable](smtp-configuration.md) y [evidencia fase 10](fase10-verification.md).
