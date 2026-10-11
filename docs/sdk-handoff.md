# Entrega al equipo LicenseHub.SDK

## Contrato inicial

OpenAPI 3.1.1 draft 1.0.0 describe los cuatro endpoints de integración y los endpoints administrativos inicialmente solicitados. URL example.invalid es deliberadamente no operativa. Los UUID y fechas de fixtures son datos de ejemplo, no credenciales ni derechos reales.

El equipo SDK puede diseñar DTOs, HttpClient y manejo de errores usando este contrato. No necesita ni debe compartir assemblies de Domain/Application. Construir un cliente para credencial opaca Bearer; no asumir OAuth ni JWT. Configurar BaseAddress HTTPS en backend y credential desde secret manager. Evitar secretos en NuGet.config, appsettings versionado o código.

Modelo sugerido de métodos asíncronos, sin implementación en este repositorio:

```csharp
Task<ActivationResponse> ActivateAsync(ActivationRequest request, Guid idempotencyKey, CancellationToken cancellationToken);
Task<EntitlementSnapshot> GetStatusAsync(string subjectId, CancellationToken cancellationToken);
Task<EntitlementSnapshot> GetFeaturesAsync(string subjectId, CancellationToken cancellationToken);
Task<WarningPage> GetWarningsAsync(string subjectId, string? cursor, int pageSize, CancellationToken cancellationToken);
```

Estos son diseños de firmas; no se afirma que existan tipos compilados. Activación no acepta applicationId/customerId. Status/features retornan el mismo snapshot. Renovación corresponde exclusivamente a la API administrativa, no a credenciales de StockMini.

## Validación local pendiente

Ejecutar desde LicenseHub, en entorno Python aislado con PyYAML, jsonschema y openapi-spec-validator instalados y versiones bloqueadas por el equipo:

```text
python contracts/validate_contract.py
```

El script valida estructura OpenAPI, referencias locales, ejemplos, algunos invariantes de caché y seguridad declarada. No verifica autenticación real, transacciones, entrega SMTP ni compatibilidad binaria del SDK. Se ejecutó el 11 de octubre de 2026 y terminó con salida 0. Antes de publicar v1, validar también con el generador C# elegido: `oneOf`, null de OpenAPI 3.1 y const deben funcionar. Si no, ajustar generación o DTOs antes de congelar el contrato.

En el repositorio SDK, fixtures deben importarse de una release versionada del contrato con checksum, nunca de una rama móvil. Baseline de compatibilidad será la primera release validada; cambios semánticos se revisan además de un diff automático.

## Matriz de pruebas para fases 2–10

| Grupo | Evidencia exigida |
|---|---|
| Unitarias | CSPRNG/formato/hash, tiempo UTC inyectable, 2 minutos, AddMonths/AddYears, lifetime, estados y derechos |
| Integración PostgreSQL | 20 canjes simultáneos y un solo commit; mismo idempotency key con cuerpo igual/distinto; replay tras cancelación HTTP; renovación bajo bloqueo |
| Seguridad | Credencial de A no canjea clave ni consulta sujeto de B; UUID ajeno 404; MFA/CSRF; credenciales rotadas/revocadas; scopes y aplicaciones inactivas |
| Pagos | Confirmación repetida aplica una renovación; rollback completo si falla; reference y operationId únicos; refund no reactiva |
| Notificaciones | Offsets 7/3/1 días, 2 minutos, lease/retry, versión obsoleta, deduplicación y fallo después de aceptación SMTP |
| Contrato SDK | JSON y errores tipados, 429/503, CancellationToken, null, lifetime, caché expirada fail closed y propiedades desconocidas |
| Operación | Restore de respaldo cifrado, migración desde snapshot anterior, secretos redactados y escaneo de dependencias |

## Extensiones administrativas pendientes de diseño OpenAPI

Antes de implementar sus endpoints, extender este mismo contrato draft con CRUD de aplicaciones/clientes, catálogo de features, generación/rotación/revocación de credenciales, sesiones/login/MFA/antiforgery, confirmación/reembolso de pagos, cancelación/reactivación de licencias, historial de renovaciones/activaciones, notificaciones, dashboard y exportación CSV. Su modelo y reglas están diseñados; sus rutas aún no están contratadas. No confundir los endpoints iniciales de Fase 1 con cobertura completa de la plataforma.

Rotación prevista: crear nueva credencial, probarla desde backend y revocar la anterior; coexistencia con expiración corta explícita, nunca revocar automáticamente antes de verificar transición. Exportación futura neutraliza fórmulas CSV y respeta permisos/PII. Login futuro entrega cookie y token antiforgery tras flujo MFA, sin endpoints de registro público.
