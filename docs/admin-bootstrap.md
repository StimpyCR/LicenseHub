# Inicialización privada de SuperAdmin

Este comando de fase 3 está implementado. No expone registro HTTP y no inicia un servidor web. Tras crear la cuenta, el operador puede usar el formulario de login/MFA del panel. Ver [flujo de autenticación](admin-authentication.md).

Requiere la migración aplicada, reloj del host sincronizado y acceso operativo al backend. No se ejecuta automáticamente ni desde CI. Rechaza la inicialización si ya existe cualquier usuario Identity. No cambia cuentas existentes ni restablece contraseñas. En producción se mantienen los requisitos de configuración de DataProtection, permisos mínimos y conexión privada.

En PowerShell 7, desde la raíz, preparar el material privado con el email real del operador:

```powershell
./tools/prepare-admin-bootstrap.ps1 -Email <email-del-operador>
```

Genera contraseña y clave TOTP aleatorias en `.secrets/bootstrap/admin.json` y una URI `otpauth` en `authenticator-uri.txt`, sin imprimirlas. Restringe el directorio al usuario operativo, SYSTEM y administradores en Windows, o permisos 700/600 en Unix. Ambos archivos quedan fuera del repositorio y contexto Docker. No sobrescribe una preparación existente.

El operador debe importar la clave/URI en su autenticador, guardar la contraseña en su gestor y editar el campo `code` del JSON con el código actual de seis dígitos. No pegar estos secretos en el chat, capturas o historial de comandos.

Configurar una conexión privada a la base adecuada. Para desarrollo local con la base Compose existente:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$privatePassword = [IO.File]::ReadAllText((Join-Path $PWD '.secrets/compose/db-password'))
[IO.File]::WriteAllText((Join-Path $PWD '.secrets/bootstrap/host-connection'), "Host=127.0.0.1;Database=licensehub;Username=licensehub_local;Password=$privatePassword")
$env:ConnectionStrings__LicenseHubFile = Join-Path $PWD '.secrets/bootstrap/host-connection'
$env:Bootstrap__InputFile = Join-Path $PWD '.secrets/bootstrap/admin.json'
dotnet run --project backend/src/LicenseHub.Api --no-launch-profile -- --bootstrap-admin
```

No usar `Development` como forma de eludir requisitos productivos. Con contenedor/API publicado, ejecutar el mismo argumento montando un archivo privado de entrada y las variables necesarias; no pasar contraseñas ni claves como argumentos CLI.

Una transacción PostgreSQL y un lock asesor serializan las inicializaciones. Identity valida y hashea la contraseña. Se comprueba el TOTP con el proveedor oficial de Identity antes de habilitar MFA y asignar SuperAdmin. Solo se confirma la transacción después de escribir auditoría `admin.bootstrap`. Ante código inválido, contraseña rechazada o fallo de almacenamiento, no queda una cuenta incompleta. No se marca el email como verificado ni se emiten cookies, tokens de sesión o códigos de recuperación.

El error muestra un código limitado, sin volcar solicitudes ni excepciones del proveedor. Un intento posterior a la inicialización devuelve `bootstrap_already_initialized`; la recuperación de SuperAdmin usa el procedimiento operativo privado de admin-recovery.md. La contraseña nunca se almacena en claro en PostgreSQL. La clave TOTP usa el almacén estándar de tokens Identity: proteger cifrado de discos/backups y acceso a la base; DataProtection no cifra automáticamente ese campo.

Tras éxito, eliminar los archivos de preparación y conexión con el mecanismo seguro del operador, conservando contraseña/autenticador según su política de recuperación. La pérdida del autenticador no se resuelve repitiendo bootstrap. No borrar cuentas para reabrir la inicialización.

