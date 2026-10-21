import type { Locator, Page } from '@playwright/test';

export class SharedByMe {
  readonly heading;
  readonly rows;
  readonly columns;
  readonly emptyHeading;
  readonly goToSecrets;
  readonly confirmation;
  readonly cancel;
  readonly continue;
  readonly status;
  readonly error;
  readonly retry;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Shared by me', exact: true });
    this.rows = page
      .getByRole('row')
      .filter({ has: page.getByRole('button', { name: 'Revoke', exact: true }) });
    this.columns = page.getByRole('columnheader');
    this.emptyHeading = page.getByRole('heading', {
      name: "You haven't shared anything",
      exact: true,
    });
    this.goToSecrets = page.getByRole('link', { name: 'Go to your secrets', exact: true });
    this.confirmation = page.getByRole('alertdialog');
    this.cancel = this.confirmation.getByRole('button', { name: 'Cancel', exact: true });
    this.continue = this.confirmation.getByRole('button', { name: 'Continue', exact: true });
    this.status = page.getByRole('main').getByRole('status');
    this.error = page.getByRole('main').getByRole('alert');
    this.retry = page.getByRole('button', { name: 'Try again', exact: true });
  }

  share(secretName: string) {
    return this.rows.filter({
      has: this.page.getByRole('cell', { name: secretName, exact: true }),
    });
  }

  // Cells have no accessible name of their own; the header order places them.
  secretOf(row: Locator) {
    return row.getByRole('cell').nth(0);
  }

  accessUntilOf(row: Locator) {
    return row.getByRole('cell').nth(3);
  }

  async cells(): Promise<string[][]> {
    // CSS inside one evaluateAll reads every row's cells in a single round trip, the empty
    // action column included, so the result compares directly with the Legacy page's.
    return this.rows.evaluateAll((rows) =>
      rows.map((row) =>
        [...row.querySelectorAll('td')].map((cell) => cell.textContent?.trim() ?? ''),
      ),
    );
  }

  async presentation() {
    // eslint-disable-next-line playwright/no-raw-locators -- Visual parity compares the Legacy page's layout classes.
    return this.page.locator('.page-header, .panel').evaluateAll((blocks) => {
      const look = (element: Element) => {
        const style = getComputedStyle(element);
        return [
          style.color,
          style.backgroundColor,
          style.fontFamily,
          style.fontSize,
          style.fontWeight,
          style.padding,
          style.margin,
          style.borderRadius,
          style.display,
        ];
      };
      return blocks.map((block) => ({
        text: (block as HTMLElement).innerText.replace(/\s+/g, ' ').trim(),
        style: look(block),
        parts: [
          ...block.querySelectorAll(
            'h1, h3, .page-sub, .empty-state, .empty-icon, p, th, td, .button',
          ),
        ].map((part) => [part.tagName, part.className, look(part)]),
        icons: [...block.querySelectorAll('.empty-icon svg > *')].map((shape) => [
          shape.tagName,
          [...shape.attributes].map((attribute) => [attribute.name, attribute.value]),
        ]),
      }));
    });
  }
}
