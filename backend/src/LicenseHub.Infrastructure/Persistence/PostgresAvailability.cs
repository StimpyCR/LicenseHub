using LicenseHub.Application.Availability;
using Microsoft.EntityFrameworkCore;

namespace LicenseHub.Infrastructure.Persistence;

public sealed class PostgresAvailability(LicenseHubDbContext database, bool requireRuntimeRole = false) : IStorageAvailability
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken) =>
        await database.Database.CanConnectAsync(cancellationToken)
        && !(await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
        && (!requireRuntimeRole || await database.Database.SqlQueryRaw<bool>("""
            SELECT NOT (
                rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls
                OR pg_has_role(current_user, 'pg_read_server_files', 'MEMBER')
                OR pg_has_role(current_user, 'pg_write_server_files', 'MEMBER')
                OR pg_has_role(current_user, 'pg_execute_server_program', 'MEMBER')
                OR EXISTS (
                    SELECT 1 FROM pg_roles reachable
                    WHERE pg_has_role(current_user, reachable.oid, 'MEMBER')
                    AND (
                        reachable.rolsuper OR reachable.rolcreatedb OR reachable.rolcreaterole
                        OR reachable.rolreplication OR reachable.rolbypassrls
                        OR has_database_privilege(reachable.oid, current_database(), 'CREATE')
                        OR EXISTS (
                            SELECT 1 FROM pg_namespace
                            WHERE nspname !~ '^pg_' AND nspname <> 'information_schema'
                            AND has_schema_privilege(reachable.oid, oid, 'CREATE')
                        )
                    )
                )
            ) AS "Value"
            FROM pg_roles WHERE rolname = current_user
            """).SingleAsync(cancellationToken));
}
