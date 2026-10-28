import { HttpTestingController } from '@angular/common/http/testing';
import type { Profile, PreparedOperation } from './profile-types';

export const profile: Profile = {
  username: 'morgan',
  displayName: 'Morgan',
  email: 'morgan@example.test',
  clearance: 'Standard',
  project: 'GLOBAL',
  avatarUrl: '/Profile/Avatar?v=1',
  hasCustomAvatar: false,
  authenticatorEnrolled: true,
  recoveryCodesRemaining: 2,
  passkeys: [],
  verificationMethods: ['PasswordAndAuthenticatorCode', 'PasswordAndRecoveryCode', 'Passkey'],
  changes: {
    changePassword: { available: true, unavailableReason: null },
    resetAuthenticator: { available: true, unavailableReason: null },
    replaceRecoveryCodes: { available: true, unavailableReason: null },
    addPasskey: { available: true, unavailableReason: null },
    changeEmail: { available: true, unavailableReason: null, explanation: null },
  },
  pendingEmail: null,
};
export const operation: PreparedOperation = {
  operationId: '11111111-1111-1111-1111-111111111111',
  kind: 'ChangePassword',
  expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
  methods: profile.verificationMethods,
};

export function button(page: HTMLElement, text: string): HTMLButtonElement {
  return [...page.querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === text,
  )!;
}
export function fill(page: HTMLElement, id: string, value: string): void {
  const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}
export function dialogs(page: HTMLElement): void {
  page.querySelectorAll('dialog').forEach((dialog) => {
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
  });
}
export function refreshed(backend: HttpTestingController, value = profile): void {
  backend.expectOne('/api/ui/me').flush({});
  backend.expectOne('/api/ui/profile').flush(value);
}
