import type { Page } from '@playwright/test';

export class ServiceAccount {
  readonly heading;
  readonly expiry;
  readonly ceiling;
  readonly projectScope;
  readonly metadataOnly;
  readonly allowedRanges;
  readonly issue;
  readonly token;
  readonly copy;
  readonly back;
  readonly enable;
  readonly disable;
  readonly remove;
  readonly revoke;
  readonly confirmation;
  readonly stepUp;
  readonly status;
  readonly scope;
  readonly keyScopes;
  readonly dangerZone;
  constructor(
    private readonly page: Page,
    name: string,
  ) {
    this.heading = page.getByRole('heading', { name, exact: true });
    this.expiry = page.getByLabel('Expires in (days)', { exact: true });
    this.ceiling = page.getByLabel('Clearance ceiling', { exact: true });
    this.projectScope = page.getByLabel('Project scope', { exact: true });
    this.metadataOnly = page.getByRole('checkbox', {
      name: 'Metadata only — can list, cannot read values',
    });
    this.allowedRanges = page.getByLabel('Allowed source ranges', { exact: false });
    this.issue = page.getByRole('button', { name: 'Issue new key', exact: true });
    this.token = page.getByLabel('New API key', { exact: true });
    this.copy = page.getByRole('button', { name: 'Copy', exact: true });
    this.back = page.getByRole('link', { name: 'Back', exact: true });
    this.enable = page.getByRole('button', { name: 'Enable', exact: true });
    this.disable = page.getByRole('button', { name: 'Disable', exact: true });
    this.remove = page.getByRole('button', { name: 'Delete', exact: true });
    this.revoke = page.getByRole('button', { name: 'Revoke', exact: true });
    this.scope = page
      .getByRole('cell')
      .filter({ has: page.getByText('metadata-only', { exact: true }) });
    // These wrappers own the Legacy layout measured by the visual parity tests.
    // eslint-disable-next-line playwright/no-raw-locators -- Scope wrapping is a visual CSS contract.
    this.keyScopes = page.locator('.key-scopes');
    // eslint-disable-next-line playwright/no-raw-locators -- The destructive panel owns its border.
    this.dangerZone = page.locator('.danger-zone');
    this.confirmation = page.getByRole('alertdialog');
    this.stepUp = page.getByRole('dialog', { name: "Confirm it's you" });
    // eslint-disable-next-line playwright/no-raw-locators -- Loading has its own status; this is the shared outcome announcer.
    this.status = page.locator('app-status-line').getByRole('status');
  }
  async scoped(sourceIp: string): Promise<void> {
    await this.expiry.fill('7');
    await this.ceiling.selectOption({ label: 'at most Standard' });
    await this.projectScope.fill(' global ');
    await this.metadataOnly.check();
    await this.allowedRanges.fill(sourceIp);
  }
  async cancelProof(): Promise<void> {
    await this.stepUp.getByRole('button', { name: 'Cancel', exact: true }).click();
  }
  async prove(code: string): Promise<void> {
    await this.stepUp.getByLabel('Authenticator code').fill(code);
    await this.stepUp.getByRole('button', { name: 'Verify and continue', exact: true }).click();
  }
  async confirm(): Promise<void> {
    await this.confirmation.getByRole('button', { name: 'Continue', exact: true }).click();
  }
  async cancel(): Promise<void> {
    await this.confirmation.getByRole('button', { name: 'Cancel', exact: true }).click();
  }
  async sourceIp(): Promise<string> {
    const response = await this.page.request.get('/api/ui/access');
    const body: unknown = await response.json();
    if (
      typeof body !== 'object' ||
      body === null ||
      !('sourceIp' in body) ||
      typeof body.sourceIp !== 'string'
    )
      throw new Error('Access did not return a source address');
    return body.sourceIp;
  }
  async copiedVisibleKey(): Promise<boolean> {
    return this.token.evaluate(
      async (element) => element.textContent === (await navigator.clipboard.readText()),
    );
  }
}
