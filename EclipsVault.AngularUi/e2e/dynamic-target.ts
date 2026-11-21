import { execFileSync } from 'node:child_process';

/** The roles this run seeds: one each side of the step-up threshold. */
export const dynamicRoles = {
  internal: 'e2e_dyn_internal',
  secret: 'e2e_dyn_secret',
} as const;

// The Development seed's reader recipe and its revocation, which is safe to run again.
const readerRecipe = `CREATE LOGIN [{{name}}] WITH PASSWORD = '{{password}}';
CREATE USER [{{name}}] FOR LOGIN [{{name}}];
ALTER ROLE db_datareader ADD MEMBER [{{name}}];`;
const revocation = `DECLARE @sessions nvarchar(max) = N'';
SELECT @sessions = @sessions + N'KILL ' + CAST(session_id AS nvarchar(10)) + N';'
FROM sys.dm_exec_sessions WHERE login_name = N'{{name}}';
IF LEN(@sessions) > 0 EXEC sp_executesql @sessions;
DROP USER IF EXISTS [{{name}}];
IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{{name}}') DROP LOGIN [{{name}}];`;

const literal = (text: string): string => `N'${text.replaceAll("'", "''")}'`;

/**
 * Runs a script on this run's SQL Server as sa. The script goes in on stdin and the password stays
 * inside the container, where the shell reads it from its own environment.
 */
function runSql(script: string): string {
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('Run through npm run e2e, which names the compose project');
  return execFileSync(
    'docker',
    [
      'compose',
      '--project-name',
      project,
      '--file',
      'e2e/compose.yml',
      'exec',
      '-T',
      'sql',
      'sh',
      '-c',
      '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -x -h -1 -i /dev/stdin',
    ],
    { encoding: 'utf8', input: script },
  );
}

/**
 * Creates the empty database the vault mints logins in (never the vault's own) and the two roles. A
 * production vault has no screen that creates roles; an operator provisions them in its database.
 */
export function provisionDynamicTarget(): void {
  const role = (name: string, sensitivity: number): string => `
INSERT INTO EclipsVault.dbo.DynamicSecretRoles
  (Id, Name, Description, ProjectKey, Environment, Sensitivity, Backend, CreationStatements,
   RevocationStatements, DefaultTtlMinutes, MaxTtlMinutes, IsEnabled, CreatedAtUtc)
SELECT NEWID(), ${literal(name)}, N'Read-only login on the run''s target database.', N'GLOBAL', 1,
  ${sensitivity}, 1, ${literal(readerRecipe)}, ${literal(revocation)}, 15, 60, 1, SYSDATETIMEOFFSET()
WHERE NOT EXISTS (SELECT 1 FROM EclipsVault.dbo.DynamicSecretRoles WHERE Name = ${literal(name)});`;
  const internal = 1;
  const secret = 3;
  runSql(`SET NOCOUNT ON;
IF DB_ID(N'DynamicTarget') IS NULL CREATE DATABASE [DynamicTarget];
GO
${role(dynamicRoles.internal, internal)}
${role(dynamicRoles.secret, secret)}
GO`);
}

/** Whether a login exists on the target, read as the target's operator would. */
export function loginExists(login: string): boolean {
  if (!/^ev_\w+$/.test(login)) throw new Error('Minted logins are plain identifiers');
  const count = runSql(
    `SET NOCOUNT ON; SELECT COUNT(*) FROM sys.server_principals WHERE name = N'${login}';`,
  );
  return count.trim() === '1';
}
