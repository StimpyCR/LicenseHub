# Despliegue

Esta versión es una base de fase 2, no un producto listo para producción. No se autoriza ni realiza despliegue productivo. El workflow de CI solo valida/compila/prueba, sin credenciales de despliegue.

Preparar ambientes separados; imágenes con digest y SBOM después de validar Docker. Configurar HTTPS en ingress, TLS moderno, DNS, certificado renovable, origen único del panel/API, AllowedHosts restringido al dominio real, KnownProxies explícitos, HSTS y secretos administrados. No usar el compose Development como plantilla productiva sin endurecimiento. Persistir y cifrar DataProtection, configurar cuenta SQL mínima y aplicar migraciones con identidad separada y revisión de script.

Una sonda live no consulta base; ready comprueba conexión y migraciones. No dirigir tráfico hasta ready. El backend no aplica migraciones al arrancar; coordinar migraciones y releases. Hangfire dashboard no se expone. Servicios de correo, alertas y workers se habilitan solamente después de implementar y verificar outbox/reintentos.

Gates: endpoints reales cubiertos por contrato; MFA/bootstrap/CSRF/BOLA y scopes probados; SDK validado; canjes concurrentes en PostgreSQL; pagos/renovaciones idempotentes; cache/revocación documentada; dependencias sin vulnerabilidades conocidas aceptables; backup/restore ensayado; pruebas responsive/PWA en plataformas objetivo. Registrar evidencia y solicitar autorización antes de producción.
