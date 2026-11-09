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

/** Finds the provisioning email this run's capture server received for `username`. */
export async function provisioningEmail(
  username: string,
): Promise<{ address: string; subject: string }> {
  for (const message of await captured('Your EclipsVault account is ready')) {
    if ((await bodyOf(message)).includes(`username '${username}'`))
      return { address: message.To[0]!.Address, subject: message.Subject };
  }
  throw new Error(`No provisioning email for ${username} reached the capture server`);
}

async function captured(subject: string): Promise<CapturedMessage[]> {
  const inbox = (await (await fetch(`${captureUrl()}/api/v1/messages`)).json()) as {
    messages: CapturedMessage[];
  };
  return inbox.messages.filter((mail) => mail.Subject === subject);
}

async function bodyOf(message: CapturedMessage): Promise<string> {
  const body = (await (await fetch(`${captureUrl()}/api/v1/message/${message.ID}`)).json()) as {
    Text: string;
  };
  return body.Text;
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
