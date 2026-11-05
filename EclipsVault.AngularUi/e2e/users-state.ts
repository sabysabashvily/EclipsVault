import { execFileSync } from 'node:child_process';

/** Lockout cannot be manufactured in a production screen without spending the source throttle. */
export function lockAccount(username: string): void {
  if (!/^[a-z0-9-]+$/.test(username)) throw new Error('Expected a test username');
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('Use the browser runner disposable stack');
  const query = `SET NOCOUNT ON; UPDATE Users SET LockedUntilUtc = DATEADD(minute, 15, SYSUTCDATETIME()), FailedAccessCount = 5 WHERE Username = '${username}'; SELECT @@ROWCOUNT;`;
  const result = execFileSync(
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
      `/opt/mssql-tools18/bin/sqlcmd -C -I -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d EclipsVault -h -1 -Q "${query}"`,
    ],
    { encoding: 'utf8' },
  );
  if (result.trim() !== '1') throw new Error('The disposable account was not locked');
}
