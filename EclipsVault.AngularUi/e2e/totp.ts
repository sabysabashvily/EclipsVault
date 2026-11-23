import { request as playwrightRequest, type APIRequestContext } from '@playwright/test';
import { createHash, createHmac } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { setTimeout } from 'node:timers/promises';

const resultsDirectory = process.env['E2E_TEST_RESULTS'] || 'test-results';
// Each Playwright project runs in a new worker, so the steps sent survive on disk for this run.
const sentStepsFile = `${resultsDirectory}/authenticator-steps-sent.json`;

// RFC 6238: HMAC-SHA1, 30 seconds, six digits. The key comes only from the vault's enrollment UI.
export function totp(secret: string, time = Date.now()): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...secret.replace(/=+$/, '')]
    .map((character) => {
      const value = alphabet.indexOf(character);
      if (value < 0) throw new Error('Invalid Base32 authenticator secret');
      return value.toString(2).padStart(5, '0');
    })
    .join('');
  const key = Buffer.from(bits.match(/.{8}/g)!.map((byte) => parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(time / 30_000)));
  const digest = createHmac('sha1', key).update(counter).digest();
  const offset = digest[digest.length - 1] & 15;
  return ((digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0');
}

/** The 30-second step a code generated now belongs to; replay protection refuses it again. */
export function currentTotpStep(): number {
  return Math.floor(Date.now() / 30_000);
}

/**
 * A code the vault accepts and has not spent, chosen by the vault's clock as `serverTotp` does, for
 * callers with no browser at hand. `previousStep` is the newest step the caller knows was spent.
 */
export async function freshTotp(secret: string, previousStep = -1): Promise<string> {
  const baseURL = process.env['E2E_BASE_URL'];
  if (!baseURL) throw new Error('E2E_BASE_URL is required to read the vault clock');
  const vault = await playwrightRequest.newContext({ baseURL, ignoreHTTPSErrors: true });
  try {
    return (await serverTotp(vault, secret, previousStep)).code;
  } finally {
    await vault.dispose();
  }
}

/**
 * A code the vault accepts and has not spent, timed by the vault's clock rather than this host's.
 * The vault accepts one step either side of its own and never a step at or below the spent one: the
 * newer of `spentStep` and the last step any helper sent for `secret` in this run. So this takes the
 * vault's current step, or the step after the spent one while that is at most one step ahead. Only
 * when even that is spent does it wait, by the vault's clock, for at most one step.
 */
export async function serverTotp(
  request: APIRequestContext,
  secret: string,
  spentStep = -1,
): Promise<{ code: string; step: number }> {
  const spent = Math.max(spentStep, lastSentStep(secret));
  for (;;) {
    const sent = Date.parse((await request.get('/favicon.svg')).headers()['date'] ?? '');
    if (!Number.isFinite(sent)) throw new Error('The vault sent no readable Date header');
    const vaultStep = Math.floor(sent / 30_000);
    const step = Math.max(vaultStep, spent + 1);
    if (step <= vaultStep + 1) {
      recordSentStep(secret, step);
      return { code: totp(secret, step * 30_000), step };
    }
    // The Date header drops the milliseconds, so the vault is at least this far into its step.
    await setTimeout(spent * 30_000 - sent);
  }
}

function lastSentStep(secret: string): number {
  return sentSteps()[secretKey(secret)] ?? -1;
}

function recordSentStep(secret: string, step: number): void {
  mkdirSync(resultsDirectory, { recursive: true });
  writeFileSync(sentStepsFile, JSON.stringify({ ...sentSteps(), [secretKey(secret)]: step }));
}

function sentSteps(): Record<string, number> {
  try {
    return JSON.parse(readFileSync(sentStepsFile, 'utf8')) as Record<string, number>;
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return {};
    throw error;
  }
}

/** The file holds a hash, never the authenticator secret itself. */
function secretKey(secret: string): string {
  return createHash('sha256').update(secret).digest('hex');
}
