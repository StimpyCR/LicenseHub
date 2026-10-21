# Instrucciones del proyecto

- Implementar LicenseHub por fases y mantener el contrato OpenAPI sincronizado.
- LicenseHub.SDK se desarrolla en un repositorio independiente; no implementarlo aquí.
- Al finalizar una fase, ejecutar sus verificaciones, documentar resultados y pendientes, crear un commit y hacer push a origin en GitHub. El operador autorizó este flujo el 12 de octubre de 2026.
- No marcar una fase completa si quedan requisitos pendientes. Los commits intermedios deben identificarse como avances.
- Antes de staging, verificar que secretos, credenciales, certificados privados y archivos de bootstrap estén excluidos. No versionar .secrets ni salidas de compilación.
- No desplegar automáticamente a producción.
- Los ensayos Docker no deben acumular contenedores, volúmenes, imágenes o caché innecesarios. Al cerrar un ensayo, retirar solo sus recursos desechables identificados y sin referencias compartidas; conservar evidencia ligera. Nunca usar una poda global ni tocar otros proyectos. Proteger siempre el contenedor/volumen indicado por el marcador de base activa, DataProtection, Mailpit, respaldos y recursos de reversión que aún sean necesarios.
- Docker Desktop comparte almacenamiento con StockMini. Desde la migración autorizada del 20 de octubre de 2026, el VHD operativo está en `D:\DockerData\disk\docker_data.vhdx`. No asumir la antigua ubicación de C:, ni aplicar scripts de mantenimiento con rutas obsoletas. Conservar los respaldos privados de `D:\Docker-Maintenance-Backups`; consultar `docs/docker-maintenance.md` antes de operaciones sobre el almacenamiento compartido.
