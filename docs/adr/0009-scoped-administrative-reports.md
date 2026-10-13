# ADR 0009: informes administrativos por aplicación

Estado: aceptado para API v1.

Los informes usan un servicio de aplicación separado, IAdminReports, para evitar incorporar consultas y exportación al flujo transaccional de activación. Cada operación exige un permiso emparejado con la aplicación; no se acepta la unión de permisos de distintas aplicaciones. La auditoría de autenticación sin applicationId queda reservada a las herramientas operativas privadas.

Los conteos reflejan expiración efectiva y no dependen de que Hangfire haya actualizado una fila. Se informa el instante UTC de evaluación, sin prometer snapshot contable entre varias consultas. La auditoría utiliza paginación acotada con cursor protegido y ligado al filtro. Nuevas acciones pueden desplazar páginas; no se conserva una sesión de snapshot de larga duración.

La exportación CSV lee como máximo 10001 filas, rechaza el exceso sobre 10000 y audita únicamente una exportación preparada con éxito. No incluye secretos. Las celdas usan comillas escapadas y prefijo neutralizador cuando podrían interpretarse como fórmulas o contienen controles de línea. El consumidor conserva la responsabilidad de custodiar los datos descargados.

La búsqueda de clientes usa expresiones EF parametrizadas y coincidencias literales; no recibe SQL ni patrones de comodines. La actualización de la PWA muestra un aviso y requiere recarga voluntaria, porque los códigos recién emitidos solo se muestran una vez y los formularios pueden contener cambios sin guardar.
