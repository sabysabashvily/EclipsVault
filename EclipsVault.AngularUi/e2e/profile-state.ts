import { execFileSync } from 'node:child_process';

/** Expiry cannot be reached through a production screen, so only this run's disposable DB is changed. */
export function expireProfileOperation(operationId: string): void {
  expire(operationId, 'ExpiresAtUtc', 1);
}

export function expireMailboxCode(operationId: string): void {
  expire(operationId, 'MailboxExpiresAtUtc', 1);
}

export function expireAccountGrant(operationId: string): void {
  expire(operationId, 'VerifiedAtUtc', 6);
}

export function expirePasskeyCeremony(operationId: string): void {
  expire(operationId, 'ChallengeExpiresAtUtc', 1);
}

function expire(
  operationId: string,
  field: 'ExpiresAtUtc' | 'MailboxExpiresAtUtc' | 'VerifiedAtUtc' | 'ChallengeExpiresAtUtc',
  minutes: number,
): void {
  if (!/^[0-9a-f-]{36}$/i.test(operationId)) throw new Error('Expected a prepared operation id');
  const query = `SET NOCOUNT ON; UPDATE ProfileOperations SET ${field} = DATEADD(minute, -${minutes}, SYSUTCDATETIME()) WHERE Id = '${operationId}'; SELECT @@ROWCOUNT;`;
  const result = database(query);
  if (result.trim() !== '1')
    throw new Error(`The prepared operation was not expired; the database answered: ${result}`);
}

/** Only the runner's own SMTP container is stopped, then started in the test's finally block. */
export function setSmtpAvailable(available: boolean): void {
  composeAction('smtp', available ? 'start' : 'stop');
}

let sessionStorePaused = false;

/**
 * Pausing preserves the disposable store and its endpoint while making real reads time out. A test
 * may resume the store mid-flow and again in its finally block, so asking for the current state is a
 * no-op rather than a Docker error.
 */
export function pauseSessionStore(paused: boolean): void {
  if (paused === sessionStorePaused) return;
  composeAction('redis', paused ? 'pause' : 'unpause');
  sessionStorePaused = paused;
}

function composeAction(
  service: 'redis' | 'smtp',
  action: 'start' | 'stop' | 'pause' | 'unpause',
): void {
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('The browser runner must name its own disposable stack');
  execFileSync(
    'docker',
    ['compose', '--project-name', project, '--file', 'e2e/compose.yml', action, service],
    { stdio: 'pipe' },
  );
}

function database(query: string): string {
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('The browser runner must name its own disposable stack');
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
      // -I turns QUOTED_IDENTIFIER on, which SQL Server requires to update a table with a filtered index.
      `/opt/mssql-tools18/bin/sqlcmd -C -I -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d EclipsVault -h -1 -Q "${query}"`,
    ],
    { encoding: 'utf8' },
  ).trim();
}

export function rejectAuditWrites(reject: boolean): void {
  // A successful ALTER prints nothing; anything else means audit writes were not changed.
  const answer = database(
    reject
      ? 'ALTER TABLE dbo.AuditLogs WITH NOCHECK ADD CONSTRAINT RejectBrowserAuditWrites CHECK (1 = 0);'
      : 'ALTER TABLE dbo.AuditLogs DROP CONSTRAINT RejectBrowserAuditWrites;',
  );
  if (answer) throw new Error(`Audit writes were not changed; the database answered: ${answer}`);
}

export function profileCredentialGeneration(): number {
  return Number(
    database(
      "SET NOCOUNT ON; SELECT CredentialGeneration FROM Users WHERE Username = 'e2e-profile-owner';",
    ),
  );
}

let smtpTransportPaused = false;

/** Like pauseSessionStore: repeating the current state is a no-op, so a finally block can resume. */
export function pauseSmtpTransport(paused: boolean): void {
  if (paused === smtpTransportPaused) return;
  composeAction('smtp', paused ? 'pause' : 'unpause');
  smtpTransportPaused = paused;
}
