# Revisión local de seguridad

**Estado posterior del 15 de octubre:** este documento conserva la revisión histórica del 13. PostgreSQL builtin ya se migró localmente con autorización y cero hallazgos reportados; ver local-migration-2026-10-15.md. Los nuevos artefactos ensayados tienen API 0 críticas/altas, 14 medias/5 bajas sin FixedVersion indicado, y frontend final 0 hallazgos tras parche estable. No son un despliegue de esos artefactos sobre la instalación principal. Ver [aceptación y riesgo residual actuales](phase10-local-acceptance.md). Los siguientes contadores/configuración son históricos, no el estado vigente.

Revisión ejecutada el 13 de octubre de 2026. El operador rechazó exportar inventarios/SBOM a Docker Scout. Se respetó: Scout queda opcional y no se ejecuta. No se subieron imágenes, SBOM ni informes detallados a servicios de análisis.

Trivy se descargó como imagen pública fijada por digest. La base pública de avisos se descargó por separado, sin montar repositorio/imágenes/secretos. Los análisis se ejecutaron con network none, telemetría/version checks deshabilitados, rootfs de solo lectura, UID sin privilegios, cap_drop ALL, sin socket Docker y con archivos de entrada locales. No requieren contactar APIs para identificar dependencias. Ver [documentación oficial del modo offline](https://www.trivy.dev/docs/v0.55/guide/advanced/air-gap/).

Los informes privados, IDs de imágenes, versiones detalladas y paquetes afectados están en .tooling/security; no se versionan ni publican como artefactos CI. Este documento es únicamente un resumen sin SBOM/inventario detallado.

| Imagen en uso | Críticas | Altas | Medias | Bajas |
| --- | ---: | ---: | ---: | ---: |
| API | 0 | 0 | 9 | 5 |
| Web | 0 | 0 | 1 | 0 |
| PostgreSQL actual | 2 | 87 | 137 | 135 |
| Mailpit | 0 | 0 | 0 | 0 |
| Proxy Mailpit | 0 | 0 | 1 | 0 |

Son incidencias por paquete/CVE; una CVE puede aparecer en varios paquetes y no equivalen a igual número de vulnerabilidades únicas. No se evaluó exhaustivamente alcanzabilidad/explotabilidad ni se suprimieron avisos sin evidencia. Un FixedVersion vacío no certifica seguridad ni disponibilidad de parche. PostgreSQL mantiene hallazgos críticos/altos y bloquea declarar preparación productiva. No se sustituyó su imagen ni se aplicaron actualizaciones dentro del contenedor, de acuerdo con la instrucción del operador. Preparar una alternativa y ensayar backup/restore/collations antes de solicitar cambio concreto.

Lockfiles de backend/tests/consumidor de pruebas y frontend, con dependencias de desarrollo: siete analizados, cero incidencias según esta base. El runtime API se inspeccionó localmente. Resultados anteriores de NuGet/npm no sustituyen el escaneo de bibliotecas del sistema operativo. No se volvió a enviar árbol npm a un servicio de auditoría; CI usa Trivy sin red para ese gate. Descargar herramientas/avisos/paquetes públicos para compilar no supone exportar inventarios privados.

Contenedores actuales sin modo privileged. API/web ejecutan con usuario no root; PostgreSQL tiene entrypoint inicial, pero su proceso principal fue comprobado con UID no root. Mailpit y su proxy eliminan capacidades y fijan rootfs solo lectura/no-new-privileges. API/web/DB Development aún tienen rootfs escribible y configuración de capacidades estándar; la plantilla productiva endurecida no se aplicó automáticamente. El usuario SQL Development sigue privilegiado; la separación runtime/migrador solo fue ensayada en restauración aislada. Estas diferencias deben resolverse con revisión y autorización de cambios efectivos.

Puertos publicados solo en loopback: panel HTTPS 8443, PostgreSQL 5432, buzón Mailpit 8025 y PostgreSQL de pruebas en puerto dinámico. API 8080 y SMTP 1025 no publicados; Mailpit no tiene salida. Sondas, TLS local confiable y rechazo de Host ajeno del buzón comprobados. El acceso local no equivale a aislamiento frente a un usuario malicioso del mismo equipo; usar datos sintéticos.

Repetir el análisis:

```powershell
./tools/scan-images-local.ps1 -UpdateDatabase
./tools/scan-dependencies-local.ps1
```

La descarga monta solo la base pública; los análisis posteriores tienen la red deshabilitada. Reportes completos quedan privados y los .tar temporales se eliminan. Actualizar base antes de decisiones productivas. El escaneo no es pentest ni certifica ausencia de vulnerabilidades desconocidas. Seguir con las incidencias de PostgreSQL y las configuraciones del entorno real antes de cerrar fase 10.


Se descargó y analizó una candidata PostgreSQL 17 Alpine sin iniciar contenedor ni cambiar la DB: 1 incidencia crítica, 24 altas, 28 medias y 3 bajas. También requiere revisión; no se propone reemplazar automáticamente la imagen actual ni se afirma compatibilidad de collations/extensiones entre distribuciones. Versiones/paquetes/CVE específicos en el informe privado .tooling/security/findings.md.


Se preparó después una candidata no root sin helper de cambio de usuario: 0 críticas/altas y 1 media en filesystem final, restauración real y 79 pruebas aprobadas. Se detectó diferencia de ordenación glibc/musl que impide asumir equivalencia. No se sustituyó el servicio actual. Ver postgres-candidate.md; el detalle sigue privado.
