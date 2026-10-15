# Copias cifradas y preparación para almacenamiento externo

El proveedor externo sigue sin seleccionar por decisión del operador. No hay cuenta cloud, credenciales de almacenamiento, conexión externa, compras ni programación automática. El formato portable es el archivo AES-GCM `.lhbk` de `tools/backup-restore.ps1`; la clave AES se custodia por separado. Los certificados Data Protection productivos, sus contraseñas y sus versiones anteriores también requieren custodia independiente: la herramienta local no los incluye en el archivo.

## Transporte disponible y límites

`tools/backup_copies.py` admite un directorio explícito existente. Sirve para ensayar localmente y posteriormente para un almacenamiento montado/autenticado por el operador. No implementa ni afirma tener un proveedor S3, Azure o SFTP integrado. Si se elige una API de objetos, añadir un adaptador concreto con permisos mínimos y las mismas condiciones de integridad/no sobrescritura; evitar credenciales en argumentos, logs o repositorio.

La copia acepta únicamente archivos regulares con nombre/formato de envoltura LicenseHub. Rechaza claves y configuración privada; nunca copia una carpeta completa. Crea un directorio nuevo por nombre y hash SHA256, verifica los bytes recibidos, fuerza flush/fsync y genera manifest. Los reintentos con la misma copia son seguros; una copia distinta o incompleta se conserva y rechaza. No sobrescribe ni elimina respaldos anteriores.

**La comprobación de cabecera/hash no autentica el contenido AES-GCM**. La prueba de restauración con la clave independiente comprueba autenticidad, manifest, integridad de la base y keyring. Ambas verificaciones son necesarias; descargar o copiar un archivo no acredita que se pueda recuperar el sistema.

```powershell
# Destino creado/revisado por el operador; el ejemplo no configura un servicio.
python tools/backup_copies.py copy '<ruta absoluta a archivo .lhbk>' '<directorio absoluto autorizado>'
python tools/backup_copies.py retention-plan '<directorio autorizado>' --keep-last 30 --minimum-age-days 30
./tools/backup-restore.ps1 -Mode RestoreTest -Archive '<archivo copiado .lhbk>'
```

El plan de retención es **solo una propuesta, sin comando de borrado**. Conserva como mínimo las últimas N copias y todas las más recientes que la edad indicada; N debe ser al menos 2. Los valores por defecto son parámetros de herramienta, no una política operativa acordada. No ejecuta limpieza, no elimina el origen y no establece automáticamente RPO/RTO. Una retención real requiere decisión del operador y protección ante ransomware/borrado de la cuenta principal.

## Al elegir un destino

1. Definir frecuencia, RPO/RTO, retención, presupuesto y ubicación/jurisdicción. Separar copias del equipo/volumen original y de la custodia de claves.
2. Usar transporte autenticado y cifrado, validando TLS/host; identidad exclusiva con permisos mínimos, preferentemente sin borrado ni sobrescritura y con versionado/inmutabilidad cuando exista. No desactivar validación de certificados para conectar un proveedor.
3. Configurar secretos fuera de Git. Para un montaje, revisar cifrado/autenticación/ACL del cliente que lo monta: la herramienta de directorio no los configura ni garantiza por sí misma.
4. Encadenar backup, copia, verificación y monitorización. Código de salida distinto de cero significa fallo; avisar por el canal operativo que se acuerde. Nunca marcar éxito solo porque comenzó una transferencia. No se envían alertas reales mientras el correo externo esté pospuesto.
5. Descargar una copia del destino a un entorno aislado y restaurarla, con workers/correo deshabilitados. Comprobar también claves/certificados, permisos, MFA, contrato y recuperación de sesiones según la política elegida. Medir duración y datos recuperados. La verificación local no reemplaza esta prueba externa.

## Evidencia local

Nueve pruebas de transporte/retención acompañan a las catorce del control de escaneo: 23 pruebas Python. Cubren reintentos, alteración de copia existente, manifest ausente, rechazo de claves/texto, destino explícito y límites conservadores de retención. No hay mocks de un proveedor cloud presentados como una integración real.

Se copió un respaldo cifrado real a otro directorio privado local, se verificó su hash y se restauró: **39 tablas y 1 archivo Data Protection**. Originales y claves se conservaron. Esto prueba el recorrido local de copia/restauración, no separación geográfica ni recuperación desde un proveedor externo. La evidencia completa permanece en `.tooling/backup-copy-drill.json` y `.secrets/backup-copy-drill`.
