import type { BrowserContext } from '@playwright/test';
import { readFile } from 'node:fs/promises';

type Cookie = Awaited<ReturnType<BrowserContext['cookies']>>[number];

export async function savedAdministratorCookies(): Promise<Cookie[]> {
  const directory = process.env['E2E_TEST_RESULTS'] || 'test-results';
  const state: unknown = JSON.parse(
    await readFile(`${directory}/sessions-administrator.json`, 'utf8'),
  );
  if (
    typeof state !== 'object' ||
    state === null ||
    !('cookies' in state) ||
    !Array.isArray(state.cookies) ||
    !state.cookies.every(isCookie)
  ) {
    throw new Error('Saved administrator state must contain valid browser cookies');
  }
  return state.cookies;
}

function isCookie(value: unknown): value is Cookie {
  return (
    typeof value === 'object' &&
    value !== null &&
    'name' in value &&
    typeof value.name === 'string' &&
    'value' in value &&
    typeof value.value === 'string' &&
    'domain' in value &&
    typeof value.domain === 'string' &&
    'path' in value &&
    typeof value.path === 'string' &&
    'expires' in value &&
    typeof value.expires === 'number' &&
    Number.isFinite(value.expires) &&
    'httpOnly' in value &&
    typeof value.httpOnly === 'boolean' &&
    'secure' in value &&
    typeof value.secure === 'boolean' &&
    'sameSite' in value &&
    (value.sameSite === 'Strict' || value.sameSite === 'Lax' || value.sameSite === 'None') &&
    (!('partitionKey' in value) || typeof value.partitionKey === 'string')
  );
}
