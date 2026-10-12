# Administración privada de usuarios

Solo SuperAdmin puede consultar, crear o modificar administradores mediante `/api/v1/admin/users`. Crear una cuenta no asigna contraseña, MFA, rol global ni permisos. La creación usa Idempotency-Key; repetir el email con la misma clave devuelve su perfil actual y cambiar el cuerpo produce conflicto.

Los permisos se asignan como pares aplicación/capacidad. Capacidades implementadas: applications:read, credentials:read, credentials:manage, features:read y features:manage. No se permite modificar SuperAdmin mediante esta API. Actualizar accesos exige CSRF, If-Match y motivo; incrementa la versión, invalida sesiones y registra auditoría. Suspender una cuenta impide autenticarla y utilizar sus permisos.

## Enrolamiento operativo

Después de crear la cuenta y asignar permisos, preparar material privado:

```powershell
./tools/prepare-admin-recovery.ps1 -Mode Enrollment -UserId <uuid> -Email <email> -Reason 'Alta autorizada'
```

El archivo queda en `.secrets/enrollment/admin.json`. Registrar la clave en Microsoft Authenticator, custodiar la contraseña y completar el código TOTP vigente desde una terminal confiable. Configurar `ConnectionStrings__LicenseHubFile` y `Enrollment__InputFile` con archivos privados y ejecutar:

```powershell
dotnet run --project backend/src/LicenseHub.Api -c Release --no-launch-profile -- --enroll-admin
```

La confirmación es ENROLL:<uuid>. El comando rechaza SuperAdmin, usuarios ya enrolados o suspendidos. No asigna permisos ni cambia el email. Retirar el material temporal después del éxito. Para una cuenta ya enrolada, utilizar el procedimiento de recuperación privada; una cuenta suspendida exige reactivación administrativa previa.

## Revisión del panel

Prueba real con PostgreSQL y cuenta sintética aislada: login/MFA, creación de aplicación, emisión/ocultación/revocación de credencial, creación/desactivación de funcionalidad, creación de delegados, asignación de permiso y suspensión. Se corrigieron el envío nativo del formulario de usuarios, la actualización del selector de aplicaciones y el estado visual tras crear registros. Vista móvil 390×844 sin desbordamiento horizontal. No se alteraron credenciales del operador.

La API de prueba empleó `Testing:CookiePrefix` para separar sus cookies de las del entorno local habitual. Esta opción solo se admite en Testing; producción conserva los nombres normales. Las capturas en screenshots muestran datos sintéticos y no contienen claves.
