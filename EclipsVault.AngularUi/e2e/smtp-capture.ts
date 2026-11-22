import { execFileSync } from 'node:child_process';

interface CapturedMessage {
  ID: string;
  To: { Address: string }[];
  Subject: string;
}

/** Reads the mailbox code this run's disposable SMTP capture server received for `address`. */
export async function mailboxCode(address: string): Promise<string> {
  const message = (await captured('Confirm your EclipsVault email')).find((mail) =>
    mail.To.some((recipient) => recipient.Address === address),
  );
  if (!message) throw new Error('The confirmation message did not arrive at the capture server');
  const code = /change: ([A-F0-9]{32})/.exec(await bodyOf(message))?.[1];
  if (!code) throw new Error('The delivered message has no mailbox code');
  return code;
}

/** Reads the token in the latest invitation link this run's capture server received for `address`. */
export async function invitationToken(address: string): Promise<string> {
  const message = (await captured("You're invited to EclipsVault")).find((mail) =>
    mail.To.some((recipient) => recipient.Address === address),
  );
  if (!message) throw new Error(`No invitation for ${address} reached the capture server`);
  const token = /\/Account\/Activate#([A-Za-z0-9_-]+)/.exec(await bodyOf(message))?.[1];
  if (!token) throw new Error('The delivered invitation has no activation link');
  return token;
}

/** Counts invitation messages this run's capture server received for `address`. */
export async function invitationCount(address: string): Promise<number> {
  return (await captured("You're invited to EclipsVault")).filter((mail) =>
    mail.To.some((recipient) => recipient.Address === address),
  ).length;
}

async function captured(subject: string): Promise<CapturedMessage[]> {
  const inbox = await captureJson<{ messages: CapturedMessage[] }>('/api/v1/messages');
  return inbox.messages.filter((mail) => mail.Subject === subject);
}

async function bodyOf(message: CapturedMessage): Promise<string> {
  return (await captureJson<{ Text: string }>(`/api/v1/message/${message.ID}`)).Text;
}

/** Names the endpoint and status when the capture fails, instead of a JSON shape error later. */
async function captureJson<T>(path: string): Promise<T> {
  const response = await fetch(`${captureUrl()}${path}`);
  if (!response.ok) throw new Error(`The capture server answered ${response.status} for ${path}`);
  return (await response.json()) as T;
}

/** Looked up on every read: the Profile outage test restarts the capture on a new host port. */
function captureUrl(): string {
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('The browser runner must name its own disposable stack');
  const address = execFileSync(
    'docker',
    ['compose', '--project-name', project, '--file', 'e2e/compose.yml', 'port', 'smtp', '8025'],
    { encoding: 'utf8' },
  ).trim();
  return `http://127.0.0.1:${address.split(':').at(-1)}`;
}
