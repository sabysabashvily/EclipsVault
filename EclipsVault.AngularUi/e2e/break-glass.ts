import { execFileSync } from 'node:child_process';
import type { Page } from '@playwright/test';

/** The access-blocked page and the Recover page it links to. */
export class BreakGlass {
  readonly blockedHeading;
  readonly recoverLink;
  readonly heading;
  readonly outcome;
  readonly continueToVault;
  constructor(private readonly page: Page) {
    this.blockedHeading = page.getByRole('heading', { name: 'Access blocked', exact: true });
    this.recoverLink = page.getByRole('link', {
      name: 'Vault administrator? Recover access',
      exact: true,
    });
    this.heading = page.getByRole('heading', { name: 'Recover access', exact: true });
    this.outcome = page.getByRole('status');
    this.continueToVault = page.getByRole('button', { name: 'Continue to the vault', exact: true });
  }

  async recover(username: string, password: string, code: string): Promise<void> {
    await this.page.getByLabel('Username').fill(username);
    await this.page.getByLabel('Password').fill(password);
    await this.page.getByLabel('Authenticator code').fill(code);
    await this.page.getByRole('button', { name: 'Verify and restore access', exact: true }).click();
  }
}

/**
 * Blocks this run's own browser in its own Redis, in the record format the vault stores, as a tripped
 * decoy would. Every spec shares the browser's address, so a test that blocks it must lift the block
 * in its finally block with {@link clearRunClientBlock}.
 */
export function blockRunClient(address: string): void {
  const answer = redis(
    'SET',
    blockKey(address),
    JSON.stringify({
      Reason: "Honey-token 'E2E_BREAK_GLASS_DECOY' tripped",
      BlockedAtUtc: new Date().toISOString(),
    }),
    'NX',
  );
  if (answer.trim() !== 'OK') throw new Error(`The block on ${address} was not written`);
}

/** Removes the block and any recovery guard on this run's browser, whatever recovery left behind. */
export function clearRunClientBlock(address: string): void {
  redis('DEL', blockKey(address), guardKey(address));
}

function blockKey(address: string): string {
  return `eclipsvault:ipblock:${singleAddress(address)}`;
}

/** The recovery guard that protects a source while its block is being lifted. */
function guardKey(address: string): string {
  return `eclipsvault:ipguard:${singleAddress(address)}`;
}

function singleAddress(address: string): string {
  if (!/^\d{1,3}(\.\d{1,3}){3}$/.test(address))
    throw new Error(`Expected an IPv4 address: ${address}`);
  return `${address}/32`;
}

function redis(...command: string[]): string {
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('Run through npm run e2e, which names the compose project');
  // redis-cli inside the container reads the password from its own environment.
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
      'redis',
      'redis-cli',
      ...command,
    ],
    { encoding: 'utf8' },
  );
}
