import type { APIRequestContext } from '@playwright/test';
import { createHmac } from 'node:crypto';
import { setTimeout } from 'node:timers/promises';

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

export async function freshTotp(secret: string, previousStep = -1): Promise<string> {
  // Enrollment consumes its code. Wait for a step after `previousStep`, which can be two boundaries
  // away when the vault accepted the next step early, and avoid sending a code just as it expires.
  // The vault's replay protection stays enabled.
  for (;;) {
    const now = Date.now();
    if (Math.floor(now / 30_000) > previousStep && now % 30_000 <= 25_000) return totp(secret);
    await setTimeout(30_000 - (now % 30_000) + 100);
  }
}

/**
 * A code the vault accepts and has not spent, timed by the vault's clock rather than this host's.
 * The vault accepts one step either side of its own and never a step at or below `spentStep`, so
 * this takes its current step, or the step after `spentStep` while that is at most one step ahead.
 * Only when even that is spent does it wait, by the vault's clock, for the vault to catch up.
 */
export async function serverTotp(
  request: APIRequestContext,
  secret: string,
  spentStep = -1,
): Promise<{ code: string; step: number }> {
  for (;;) {
    const sent = Date.parse((await request.get('/favicon.svg')).headers()['date'] ?? '');
    if (!Number.isFinite(sent)) throw new Error('The vault sent no readable Date header');
    const vaultStep = Math.floor(sent / 30_000);
    const step = Math.max(vaultStep, spentStep + 1);
    if (step <= vaultStep + 1) return { code: totp(secret, step * 30_000), step };
    // The Date header drops the milliseconds, so the vault is at least this far into its step.
    await setTimeout(spentStep * 30_000 - sent);
  }
}
