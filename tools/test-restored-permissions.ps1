param([Parameter(Mandatory)][string]$Database,
    [ValidatePattern('^(licensehub-local-postgres-1|licensehub-postgres-candidate-[a-f0-9]{32}-tests)$')]
    [string]$Container = 'licensehub-local-postgres-1')
$ErrorActionPreference = 'Stop'
if ($Database -notmatch '^licensehub_restore_[a-f0-9]{32}_tests$') { throw 'Se requiere una base de restauración aislada.' }
$root = Split-Path $PSScriptRoot -Parent
$docker = Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'
$role = 'restore_probe_' + [Guid]::NewGuid().ToString('N')
$grants = [IO.File]::ReadAllText((Join-Path $root 'docker/runtime-permissions.sql'))
$verification = @'
SET ROLE :"runtime_role";
DO $$
BEGIN
    IF has_schema_privilege(current_user, 'public', 'CREATE') OR has_schema_privilege(current_user, 'hangfire', 'CREATE') THEN
        RAISE EXCEPTION 'Runtime may change schema';
    END IF;
    IF has_table_privilege(current_user, '"Licenses"', 'TRUNCATE')
       OR has_table_privilege(current_user, '"Licenses"', 'DELETE')
       OR has_table_privilege(current_user, '"AuditLogs"', 'UPDATE')
       OR has_table_privilege(current_user, '"LicenseRenewals"', 'DELETE')
       OR has_table_privilege(current_user, '"__EFMigrationsHistory"', 'INSERT') THEN
        RAISE EXCEPTION 'Runtime has excessive privileges';
    END IF;
    PERFORM count(*) FROM "Licenses";
    IF NOT has_table_privilege(current_user, '"Licenses"', 'UPDATE')
       OR NOT has_table_privilege(current_user, 'hangfire.job', 'UPDATE')
       OR NOT has_table_privilege(current_user, '"AuditLogs"', 'INSERT') THEN
        RAISE EXCEPTION 'Required runtime privileges missing';
    END IF;
    BEGIN
        UPDATE "AuditLogs" SET "Action"="Action";
        RAISE EXCEPTION 'Audit update accepted';
    EXCEPTION WHEN insufficient_privilege THEN NULL;
    END;
END $$;
RESET ROLE;
ROLLBACK;
'@
$sql = "BEGIN; CREATE ROLE $role NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;`n" + $grants + "`n" + $verification
$sql | & $docker exec -i $Container psql -U licensehub_local -d $Database -v ON_ERROR_STOP=1 -v "runtime_role=$role" -v "database_name=$Database" --quiet
if ($LASTEXITCODE -ne 0) { throw 'Falló la verificación de privilegios.' }
Write-Output 'Mínimo privilegio verificado en restauración; rol temporal y cambios revertidos con ROLLBACK.'
