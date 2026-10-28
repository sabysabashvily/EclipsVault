import type { Page } from '@playwright/test';

/** The frozen email operation through the same session and antiforgery token the Profile UI uses. */
export class MailboxProfile {
  constructor(private readonly page: Page) {}

  async post(path: string, data: object) {
    await this.page.request.get('/api/ui/me');
    const cookies = await this.page.context().cookies();
    const token = cookies.find((cookie) => cookie.name === 'XSRF-TOKEN');
    if (!token) throw new Error('The signed-in browser has no antiforgery token');
    return this.page.request.post(`/api/ui/profile/${path}`, {
      data,
      headers: { 'X-XSRF-TOKEN': decodeURIComponent(token.value) },
    });
  }

  async mailboxCode(address: string): Promise<string> {
    const capture = process.env['E2E_SMTP_URL'];
    if (!capture) throw new Error('The E2E runner must configure its disposable SMTP capture');
    const inbox = (await (await fetch(`${capture}/api/v1/messages`)).json()) as {
      messages: { ID: string; To: { Address: string }[]; Subject: string }[];
    };
    const message = inbox.messages.find(
      (mail) =>
        mail.Subject === 'Confirm your EclipsVault email' &&
        mail.To.some((recipient) => recipient.Address === address),
    );
    if (!message) throw new Error('The confirmation message did not arrive at the capture server');
    const body = (await (await fetch(`${capture}/api/v1/message/${message.ID}`)).json()) as {
      Text: string;
    };
    const code = /change: ([A-F0-9]{32})/.exec(body.Text)?.[1];
    if (!code) throw new Error('The delivered message has no mailbox code');
    return code;
  }
}
