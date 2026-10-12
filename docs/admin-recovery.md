# Recuperación operativa de administradores

Implementada exclusivamente como CLI privada, sin servidor HTTP, registro público ni ruta de recuperación remota. Sirve para pérdida del autenticador y rotación operativa de MFA. Requiere un operador con acceso confiable al host, secretos y base de datos: ese acceso es la autoridad de recuperación, no una prueba de identidad por email. En producción verificar identidad del titular y aprobar el cambio bajo control de dos operadores antes de ejecutar; limitar y auditar acceso al host/secret manager. Este procedimiento no sustituye recuperación autoservicio ni administración de otros usuarios.

La operación exige UUID y email coincidentes de un SuperAdmin existente o delegado enrolado con permisos, confirmación RECOVER:<UUID>, motivo, contraseña nueva válida, clave TOTP nueva y código verificado por Identity. Rechaza cuentas suspendidas. No cambia roles, permisos ni email, no crea usuarios y rechaza reutilizar la clave actual. Genera un reset token solo en memoria. Transacción y bloqueo del usuario serializan la recuperación con login/MFA. Rechazo de código/password revierte todos los cambios. Un éxito habilita MFA, elimina lockout, cambia SecurityStamp e invalida sesiones y desafíos anteriores, con auditoría admin.recovered y operador del sistema.

Preparar material privado desde raíz, usando UUID obtenido del inventario operativo confiable:

```powershell
./tools/prepare-admin-recovery.ps1 -UserId <uuid> -Email <email> -Reason 'Motivo documentado'
```

Genera .secrets/recovery/admin.json, sin imprimir secretos; ACL restringida en Windows o permisos 700/600 en Unix. No sobrescribe preparación anterior. Enrolar authenticatorKey nueva, guardar password en gestor, rellenar code con seis dígitos actuales. No enviar al chat ni capturar secretos.

Configurar ConnectionStrings__LicenseHubFile con archivo privado de la base correcta y Recovery__InputFile con ruta absoluta al JSON. Desde terminal confiable:

```powershell
dotnet run --project backend/src/LicenseHub.Api -c Release --no-launch-profile -- --recover-admin
```

No usar Development para eludir requisitos productivos. Producción conserva requisitos de DataProtection persistente/certificado. El comando devuelve salida 0 solo tras commit. Después, entrar con contraseña y autenticador nuevos; conservar respaldo adecuado y retirar archivos temporales privados. Una repetición con la misma clave es rechazada. La cuenta real del operador no se modifica automáticamente por tests ni por instalar este incremento.

Pruebas en bases aisladas verifican rollback MFA, cambio de contraseña, invalidez de sesión/desafío anteriores, MFA habilitado, auditoría y rechazo de repetición. No se ha ensayado recuperación sobre la cuenta real ni un procedimiento organizacional de doble control.
