#!/bin/sh
# Solo cluster sintético efímero, sin red, puertos, secretos ni volúmenes existentes.
set -eu
export PGDATA=/tmp/locale-probe
initdb -D "$PGDATA" --auth=trust --locale=en_US.utf8 >/dev/null
pg_ctl -D "$PGDATA" -o "-c listen_addresses='' -c unix_socket_directories=/tmp" -w start >/dev/null
trap 'pg_ctl -D "$PGDATA" -m fast -w stop >/dev/null' EXIT
psql -h /tmp -d postgres -v ON_ERROR_STOP=1 -At <<'SQL'
SELECT string_agg(value, ',' ORDER BY value) FROM (VALUES ('a'),('A'),('á'),('ä'),('n'),('ñ'),('z'),('Z')) AS sample(value);
SELECT lower('ÁÄÑİ'), upper('áäñı');
SELECT string_agg(value, ',' ORDER BY value) FROM (VALUES ('a-b'),('ab'),('a.b'),('a_b'),('a1'),('a01')) AS sample(value);
SELECT string_agg(value, ',' ORDER BY value) FROM (VALUES ('a-a'),('a.a'),('a_a'),('aa'),('a1'),('advanced_reports')) AS sample(value);
SELECT COALESCE(string_agg(id::text, ',' ORDER BY id), 'none') FROM (VALUES (1,'İpek'),(2,'IPEK'),(3,'ıpek'),(4,'ipek')) AS sample(id,value) WHERE strpos(lower(value),'i') > 0;
SELECT COALESCE(string_agg(id::text, ',' ORDER BY id), 'none') FROM (VALUES (1,'Cliente%_A'),(2,'ClienteAA'),(3,'Cliente%_B')) AS sample(id,value) WHERE strpos(lower(value),'%_') > 0;
SQL
