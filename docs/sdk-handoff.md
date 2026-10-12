# Entrega al equipo LicenseHub.SDK

## Contrato inicial

OpenAPI 3.1.1 draft 1.0.0 describe los cuatro endpoints de integración y rutas administrativas. example.invalid no es operativo; UUID/fechas de fixtures son ejemplos. El SDK se desarrolla en otro repositorio y no comparte assemblies Domain/Application.

Usar DTOs estables, HttpClient, errores tipados y CancellationToken. Credencial opaca Bearer, sin asumir OAuth/JWT. BaseAddress HTTPS y credencial desde secret manager en backend; jamás distribuirla en clientes web/móviles, NuGet.config o appsettings versionado.

Firmas previstas, sin implementación aquí:

```csharp
Task<ActivationResponse> ActivateAsync(ActivationRequest request, Guid idempotencyKey, CancellationToken cancellationToken);
Task<EntitlementSnapshot> GetStatusAsync(string subjectId, CancellationToken cancellationToken);
Task<EntitlementSnapshot> GetFeaturesAsync(string subjectId, CancellationToken cancellationToken);
Task<WarningPage> GetWarningsAsync(string subjectId, string? cursor, int pageSize, CancellationToken cancellationToken);
```

Activación no acepta applicationId/customerId. Status/features comparten snapshot. Renovación es administrativa. StockMini autentica al usuario y obtiene el negocio autorizado antes de llamar al SDK. El SDK no sustituye autorización del servidor.

## Validación

Instalar contracts/requirements-validation.txt en Python aislado:

```text
python contracts/validate_contract.py
python contracts/check_compatibility.py
```

Verifica OpenAPI, referencias, fixtures e invariantes. El baseline comprueba contratos de integración y requiere revisión semántica. Ambos validadores terminaron con salida 0 en este incremento; no demuestra compatibilidad binaria, SMTP ni transacciones.

Antes de publicar v1, validar con generador C# elegido: oneOf, null OpenAPI 3.1 y const. Importar fixtures desde release con checksum, nunca rama móvil. Primera release y validación de SDK real pendientes.

## Matriz de pruebas

| Grupo | Evidencia exigida |
|---|---|
| Unitarias | CSPRNG/formato/hash, UTC, 2 minutos, meses/años calendario, lifetime y estados |
| PostgreSQL | 20 canjes simultáneos, un commit; idempotencia/payload distinto; replay tras cancelación; renovación bajo bloqueo |
| Seguridad | Credencial A no consulta/canjea B; UUID ajeno 404; MFA/CSRF; rotación/revocación, scopes y aplicación inactiva |
| Pagos | Confirmación repetida aplica una renovación; rollback; reference/operationId únicos; refund no reactiva |
| Notificaciones | Offsets 7/3/1 días, corta duración, lease/retry, versión obsoleta y deduplicación |
| SDK | JSON/errores, 429/503, CancellationToken, null/lifetime, caché fail closed y propiedades desconocidas |
| Operación | Restore cifrado, migraciones anteriores, secretos redactados y escaneo de dependencias |

## Administración disponible

Implementados login/MFA/CSRF/sesión/logout y listado/creación/edición de aplicaciones, emisión/listado/revocación de credenciales. El esquema de integración resuelve aplicación/scopes desde PostgreSQL. Licencias, clientes, catálogo Premium, pagos, notificaciones, auditoría consultable, dashboard y CSV siguen pendientes.

Rotación: emitir nueva credencial, instalar/probar en backend y revocar anterior, con coexistencia/expiración explícita. Se muestra secreto una vez; replay de emisión responde 409 secret_not_recoverable. No hay registro público ni borrado de historial. Exportación futura neutralizará fórmulas CSV y respetará permisos/PII.

Ver [autenticación](integration-authentication.md), [gestión administrativa](application-management.md) y contracts/compatibility.md. No hay derechos Premium simulados disponibles para el SDK.
