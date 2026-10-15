# PostgreSQL: preparación de migración con retorno

Estado: procedimiento general. La migración **local** autorizada se ejecutó el 15 de octubre; ver [informe y retorno comprobado](local-migration-2026-10-15.md). Producción no fue migrada. La Fase 10 permanece abierta. No usar una candidata con hallazgos críticos/altos sin resolver o con diferencias de locale no aceptadas.

## Condiciones previas

1. Construir la candidata aislada y registrar privadamente image ID, paquetes y fecha de base de avisos. Descargar solo avisos públicos; ejecutar los escaneos sin red. No exportar imágenes, SBOM ni inventarios. Fijar el digest final aceptado antes de cualquier despliegue; una receta apt-get upgrade por sí sola no garantiza reconstrucción reproducible.
2. Comparar locales con tools/compare-postgres-locales.ps1. Los vectores son regresión limitada: inspeccionar también búsquedas Unicode, ordenamiento de códigos y paginación estable, índices, constraints, extensiones y versiones de collations. No asumir que un nombre de locale igual significa semántica igual.
3. Restaurar un backup cifrado reciente en un volumen NUEVO y ejecutar integridad, permisos mínimos y la suite completa contra bases sintéticas. No ejecutar API, workers o SMTP sobre el clon de datos reales. No utilizar la cuenta del operador ni sus secretos para pruebas.
4. Preparar un cambio concreto de Compose con la imagen aceptada, volumen nuevo y credenciales privadas. Conservar servicio, volumen, backup y configuración del origen. No reutilizar físicamente el volumen antiguo con otra distribución/UID.
5. Solicitar autorización para la ventana, pausa de servicios que escriben, sustitución de contenedores y cambio de conexión. La autorización para ensayos aislados no autoriza ese corte.

## Ventana autorizada

- Registrar hora UTC y configuración de origen; restringir tráfico de escritura y detener workers/API que puedan escribir. Confirmar ausencia de escritores antes del dump final. El backup de ensayo anterior no contiene necesariamente cambios recientes.
- Generar backup nuevo y conservar su clave en custodia independiente del archivo. Probar descifrado/integridad; incluir claves Data Protection y certificados necesarios sin exponerlos en logs.
- Restaurar lógicamente en destino nuevo. Aplicar permisos de runtime sin superusuario ni DDL; separar migración/propietario. Verificar historial inmutable, relaciones, conteos y readiness antes de permitir tráfico.
- Verificar HTTPS, login/MFA del operador sin cambiar credenciales, aplicación/aislamiento y consultas representativas. Cualquier prueba que cree datos utiliza fixtures explícitos. Habilitar inicialmente el mínimo de escritores y vigilar errores antes de reanudar trabajos.
- No borrar ni detener recursos ajenos; no limpiar los volúmenes conservados como parte de la migración. Retención y eliminación requieren decisión explícita.

## Retorno y punto de no retorno automático

Antes de admitir escrituras en destino, se puede volver a la conexión del origen preservado y reanudar sus procesos tras comprobar salud. No arrancar dos grupos de workers sobre dos bases divergentes.

Después de admitir escrituras en destino, el origen queda atrasado. No volver automáticamente al volumen antiguo: perdería esas operaciones. Bloquear nuevas escrituras, respaldar destino y decidir recuperación/reconciliación con revisión humana. No prometer rollback sin pérdida de datos tras ese punto.

Los tiempos de ensayo no constituyen RPO/RTO ni SLA. Falta acordar ventana, monitorización, custodia externa, retención y criterios de aceptación del despliegue real. Las pruebas PWA en dispositivos físicos y la configuración productiva siguen siendo pendientes separados.
