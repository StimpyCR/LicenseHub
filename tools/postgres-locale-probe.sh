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
SQL
