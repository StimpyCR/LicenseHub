#!/bin/sh
# Solo cluster sintético efímero, sin red, puertos, secretos ni volúmenes existentes.
set -eu
export PGDATA=/tmp/locale-probe
case "${LICENSEHUB_LOCALE_POLICY:-libc}" in
  libc) initdb -D "$PGDATA" --auth=trust --locale=en_US.utf8 >/dev/null ;;
  builtin) initdb -D "$PGDATA" --auth=trust --locale-provider=builtin --locale=C.UTF-8 --encoding=UTF8 >/dev/null ;;
  *) echo 'Unsupported synthetic locale policy' >&2; exit 2 ;;
esac
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
if [ "${LICENSEHUB_FULL_UNICODE_CASE:-0}" = 1 ]; then
    psql -h /tmp -d postgres -v ON_ERROR_STOP=1 -At <<'SQL'
WITH mapped AS (
    SELECT code, lower(chr(code)) AS lower_value, upper(chr(code)) AS upper_value
    FROM generate_series(1,1114111) AS sample(code)
    WHERE code NOT BETWEEN 55296 AND 57343
)
SELECT encode(sha256(convert_to(string_agg(
    encode(sha256(convert_to(lower_value || chr(1) || upper_value, 'UTF8')), 'hex'),
    '' ORDER BY code), 'UTF8')), 'hex')
FROM mapped;
WITH differences AS (
    SELECT code FROM generate_series(1,1114111) AS sample(code)
    WHERE code NOT BETWEEN 55296 AND 57343
    AND (lower(chr(code)) <> lower(chr(code) COLLATE pg_c_utf8)
         OR upper(chr(code)) <> upper(chr(code) COLLATE pg_c_utf8))
)
SELECT json_build_object('changedCodePoints', count(*), 'examples',
    (SELECT json_agg(code) FROM (SELECT code FROM differences ORDER BY code LIMIT 16) AS limited))
FROM differences;
SQL
fi
