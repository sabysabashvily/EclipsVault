import type { APIRequestContext } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { setTimeout } from 'node:timers/promises';

const maximumProofAgeMilliseconds = configuredProofAgeMilliseconds();
const setupAllowanceMilliseconds = 60_000;
export const savedProofSetupTimeout = maximumProofAgeMilliseconds + setupAllowanceMilliseconds;

export async function waitForSavedAdministratorProofToExpire(
  request: APIRequestContext,
): Promise<void> {
  for (;;) {
    const remaining = await millisecondsUntilSavedProofExpires(request);
    if (remaining <= 0) return;
    await setTimeout(remaining);
  }
}

async function millisecondsUntilSavedProofExpires(request: APIRequestContext): Promise<number> {
  const response = await request.get('/api/ui/sessions');
  if (response.status() !== 200) {
    throw new Error(`The saved administrator session answered ${response.status()}`);
  }
  const body: unknown = await response.json();
  if (
    typeof body !== 'object' ||
    body === null ||
    !('sessions' in body) ||
    !Array.isArray(body.sessions)
  ) {
    throw new Error('The server did not return signed-in sessions');
  }
  const sessions: unknown[] = body.sessions;
  const current = sessions.find(
    (session) =>
      typeof session === 'object' &&
      session !== null &&
      'isCurrent' in session &&
      session.isCurrent === true,
  );
  if (
    typeof current !== 'object' ||
    current === null ||
    !('createdAtUtc' in current) ||
    typeof current.createdAtUtc !== 'string'
  ) {
    throw new Error('The server did not return the current session creation time');
  }
  const createdAt = Date.parse(current.createdAtUtc);
  const serverNow = Date.parse(response.headers()['date'] ?? '');
  if (!Number.isFinite(createdAt) || !Number.isFinite(serverNow)) {
    throw new Error('The server session clock was not readable');
  }

  // The saved cookie follows passkey enrollment's replacement session, which preserves the older
  // strong-auth claims. Its creation time therefore bounds proof expiry without decrypting cookies.
  // HTTP Date has whole seconds; require the following second before testing the strict age limit.
  return createdAt + maximumProofAgeMilliseconds + 1_000 - serverNow;
}

function configuredProofAgeMilliseconds(): number {
  const settings: unknown = JSON.parse(readFileSync('../EclipsVault.Web/appsettings.json', 'utf8'));
  if (
    typeof settings !== 'object' ||
    settings === null ||
    !('StepUp' in settings) ||
    typeof settings.StepUp !== 'object' ||
    settings.StepUp === null ||
    !('MaxAuthAgeMinutes' in settings.StepUp) ||
    typeof settings.StepUp.MaxAuthAgeMinutes !== 'number' ||
    !Number.isInteger(settings.StepUp.MaxAuthAgeMinutes) ||
    settings.StepUp.MaxAuthAgeMinutes < 0
  ) {
    throw new Error('The browser stack must have a configured proof age in whole minutes');
  }
  return settings.StepUp.MaxAuthAgeMinutes * 60_000;
}
