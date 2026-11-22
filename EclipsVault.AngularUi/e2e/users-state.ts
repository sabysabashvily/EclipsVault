import { execFileSync } from 'node:child_process';

/** Lockout cannot be manufactured in a production screen without spending the source throttle. */
export function lockAccount(username: string): void {
  const query = `SET NOCOUNT ON; UPDATE Users SET LockedUntilUtc = DATEADD(minute, 15, SYSUTCDATETIME()), FailedAccessCount = 5 WHERE Username = '${checkedUsername(username)}'; SELECT @@ROWCOUNT;`;
  if (runQuery(query) !== '1') throw new Error('The disposable account was not locked');
}

/** Expiry cannot be reached through a production screen without waiting 72 hours. */
export function expireInvitation(username: string): void {
  const query = `SET NOCOUNT ON; UPDATE AccountInvitations SET ExpiresAtUtc = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE UserId = (SELECT Id FROM Users WHERE Username = '${checkedUsername(username)}'); SELECT @@ROWCOUNT;`;
  if (runQuery(query) !== '1') throw new Error('The disposable invitation was not expired');
}

function checkedUsername(username: string): string {
  if (!/^[a-z0-9-]+$/.test(username)) throw new Error('Expected a test username');
  return username;
}

function runQuery(query: string): string {
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('Use the browser runner disposable stack');
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
      `/opt/mssql-tools18/bin/sqlcmd -C -I -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d EclipsVault -h -1 -Q "${query}"`,
    ],
    { encoding: 'utf8' },
  ).trim();
}
