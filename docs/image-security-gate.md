# Comprobación local de imagen antes de despliegue

```powershell
./tools/scan-images-local.ps1 -UpdateDatabase -Images '<imagen local>'
./.venv/Scripts/python.exe tools/check_image_security.py '<misma imagen local>'
```

El escáner descarga únicamente avisos públicos; analiza el tar local sin red/telemetría. Genera informe y archivo .evidence.json privados en .tooling/security. La evidencia liga el ID inspeccionado antes/después de la exportación, hash SHA256 del informe, fecha de avisos, finalización y procedimiento fijado. En Docker con almacén containerd el ID local puede identificar un manifiesto mientras Trivy registra la configuración: no se comparan incorrectamente como si fueran el mismo identificador.

El control no descarga imágenes ni publica resultados. Inspecciona la imagen local y exige coincidencia con la exportada; verifica hash, estructura de paquetes/OS, vigencia y ausencia de hallazgos críticos/altos. Severidades desconocidas, OS no identificado/fuera de soporte, informes incompletos, fechas inválidas/futuras y evidencia ausente bloquean. Si se actualizan los avisos después del escaneo, exige repetirlo. No convertir ausencia de datos en cero hallazgos.

La antigüedad máxima inicial es 48 horas, configurable de 1 a 168 mediante --max-age-hours; la política final del entorno debe revisarse. Medias/bajas se reportan y siguen necesitando revisión. Evidencia/informes son archivos locales confiados al operador: hashes detectan modificaciones accidentales, no son una firma ni protegen frente a un atacante que controle ambos archivos/el host. El control no prueba compatibilidad, autenticidad de una imagen publicada, escaneo exhaustivo o ausencia de vulnerabilidades desconocidas, ni autoriza despliegue.

Verificación del 15 de octubre: 14 pruebas Python pasadas y dos recorridos reales. La candidata Alpine sin helper pasó con 0 críticas/altas y 1 media; Debian patched fue rechazada con 1 crítica/61 altas. Ambas se escanearon con avisos públicos actualizados ese día. Se comprobó y corrigió la diferencia manifiesto/configuración al ensayar el primer recorrido real. No se reemplazó ningún servicio. Los resultados detallados y los dos archivos de evidencia permanecen privados y no se suben a Actions.

```powershell
./.venv/Scripts/python.exe -m unittest discover -s tools -p test_check_image_security.py
```

CI ejecuta las pruebas sintéticas de la herramienta, no el análisis de imágenes privadas. El despliegue sigue siendo manual y sujeto a autorización. También deben pasar readiness, permisos, compatibilidad y recuperación; una imagen que pasa aquí puede seguir bloqueada por esos motivos.
