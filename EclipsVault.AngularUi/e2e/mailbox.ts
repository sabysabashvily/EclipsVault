import type { Page } from '@playwright/test';

/** The frozen email operation through the same session and antiforgery token the Profile UI uses. */
export class MailboxProfile {
  constructor(private readonly page: Page) {}

  async open(): Promise<void> {
    await this.page.goto('/Profile');
  }

  snapshot() {
    return this.page.request.get('/api/ui/profile');
  }

  prepareEmailChange(address: string) {
    return this.post('operations', { kind: 'ChangeEmail', newEmail: address });
  }

  verifyWithAuthenticator(operationId: string, currentPassword: string, code: string) {
    return this.post(`operations/${operationId}/verify`, {
      method: 'PasswordAndAuthenticatorCode',
      currentPassword,
      code,
    });
  }

  requestCode(operationId: string) {
    return this.post('email/request', { operationId });
  }

  confirmEmail(operationId: string, code: string) {
    return this.post('email/confirm', { operationId, code });
  }

  private async post(path: string, data: object) {
    await this.page.request.get('/api/ui/me');
    const cookies = await this.page.context().cookies();
    const token = cookies.find((cookie) => cookie.name === 'XSRF-TOKEN');
    if (!token) throw new Error('The signed-in browser has no antiforgery token');
    return this.page.request.post(`/api/ui/profile/${path}`, {
      data,
      headers: { RequestVerificationToken: decodeURIComponent(token.value) },
    });
  }
}
