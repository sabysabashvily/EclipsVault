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
  const now = Date.now();
  // Enrollment consumes its code. Wait for a new step before the subsequent sign-in, and avoid
  // sending a code just as it expires. The vault's replay protection stays enabled.
  if (Math.floor(now / 30_000) <= previousStep || now % 30_000 > 25_000) {
    await setTimeout(30_000 - (now % 30_000) + 100);
  }
  return totp(secret);
}
