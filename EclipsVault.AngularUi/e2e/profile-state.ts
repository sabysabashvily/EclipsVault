import { execFileSync } from 'node:child_process';

/** Expiry cannot be reached through a production screen, so only this run's disposable DB is changed. */
export function expireProfileOperation(operationId: string): void {
  if (!/^[0-9a-f-]{36}$/i.test(operationId)) throw new Error('Expected a prepared operation id');
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('The browser runner must name its own disposable stack');
  const query = `SET NOCOUNT ON; UPDATE ProfileOperations SET ExpiresAtUtc = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE Id = '${operationId}'; SELECT @@ROWCOUNT;`;
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
      `/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d EclipsVault -h -1 -Q "${query}"`,
    ],
    { encoding: 'utf8' },
  );
  if (result.trim() !== '1') throw new Error('The prepared operation was not expired');
}
