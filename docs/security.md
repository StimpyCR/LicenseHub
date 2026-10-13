# Seguridad

Fases 1–9 implementadas: Identity privado sin registro público, bootstrap/recuperación offline, MFA obligatorio, lockout, desafíos únicos, replay TOTP persistente y sesión de 30 minutos contrastada con SecurityStamp. Cookies Secure/HttpOnly/Strict, CSRF y permisos por aplicación. Sin bypass MFA de Development. Ver admin-authentication.md y admin-recovery.md.

Bearer opaco de 256 bits: solo SHA256 persistido, scopes/aplicación/expiración/revocación comprobados en cada solicitud. No JWT ni secretos en Angular/móviles. El backend consumidor determina subjectId; SDK no sustituye autorización. BOLA, FK compuestas, concurrencia e idempotencia verificados con PostgreSQL/SDK reales.

HTTPS obligatorio, proxies explícitos con un salto, CORS restrictivo, cuerpo 8 KiB auth/integraciones y 128 KiB general, JSON sin campos extra/duplicados donde el contrato los prohíbe, rate limiting IP/login/MFA, errores centralizados, no-store, CSP/nosniff y logging sin cuerpos/headers/secretos. Los límites actuales son por proceso/IP, no cuotas distribuidas por aplicación; coordinar ingress antes de escalar réplicas y evaluar backends que comparten IP.

Producción rechaza AllowedHosts comodín, keyring no persistente o PFX DataProtection sin clave/vigencia. CertificatePasswordFile permite un secreto montado. Development conserva DP sin cifrar en disco; backup sí cifrado. Identity guarda TOTP en DB, no cifrado automáticamente por DP. Custodiar certificados antiguos, base y backups.

Runtime no debe ser dueño/superusuario/miembro de migrador. docker/runtime-permissions.sql ensayado en restauración aislada con rol temporal/rollback; niega modificar auditoría/activaciones/renovaciones y migraciones, no concede DDL/TRUNCATE. Los triggers no defienden contra dueño/superusuario. Development mantiene usuario original; producción debe provisionar identidades separadas.

Estado/vigencia efectivos comprobados en servidor; caché de consumidor limitada a cacheUntil (máximo 30 segundos y expiración). Después, denegar si no puede actualizar. Replay de activación no acredita derechos actuales. Pagos/renovaciones requieren autorización y operaciones únicas.

Outbox persistente, deduplicación/reintentos, SMTP STARTTLS con certificado validado; plaintext solo Development/Testing y rechazado en producción. Plantillas texto plano, errores redactados. SMTP al menos una vez puede repetir tras aceptación previa. Gmail no configurado/verificado; solo captura local autorizada.

Revisión del 13 de octubre: NuGet transitivas y npm audit nivel low sin vulnerabilidades reportadas. No es pentest ni escaneo de SO/imágenes. CI valida pruebas/contrato/build/dependencias sin desplegar. Nunca habilitar SensitiveDataLogging ni imprimir configuración privada. Secretos/PFX/dumps/TRX excluidos de Git. Pendientes reales: custodia externa, monitor/alertas, digests/SBOM/escaneo imágenes, parcheado del host, verificación del entorno productivo y dispositivos PWA. Ver fase10-verification.md.
