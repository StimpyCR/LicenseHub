-- Solo lectura. Orden binario explícito para comparar entre proveedores de locale.
BEGIN READ ONLY;
SET TIME ZONE 'UTC';
SET DateStyle = 'ISO, YMD';
SELECT format(
  'SELECT %L || ''|'' || count(*)::text || ''|'' || encode(sha256(convert_to(coalesce(string_agg(h, '''' ORDER BY h COLLATE "C"), ''''), ''UTF8'')), ''hex'') FROM (SELECT encode(sha256(convert_to(to_jsonb(t)::text, ''UTF8'')), ''hex'') h FROM %I.%I t) rows;',
  schemaname || '.' || tablename, schemaname, tablename)
FROM pg_tables WHERE schemaname IN ('public', 'hangfire')
ORDER BY schemaname COLLATE "C", tablename COLLATE "C"
\gexec
ROLLBACK;
