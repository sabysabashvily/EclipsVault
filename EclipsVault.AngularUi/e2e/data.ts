import type { Page } from '@playwright/test';
import type { components } from '../src/app/ui-api/schema.generated';

export type YourDataSummary = components['schemas']['DataResponse'];

export class YourData {
  readonly heading;
  readonly introduction;
  readonly accountValues;
  readonly download;
  readonly downloadCopy;
  readonly error;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Your data', exact: true });
    this.introduction = page.getByText('Everything EclipsVault holds about your account', {
      exact: false,
    });
    this.accountValues = page.getByRole('main').getByRole('definition');
    this.download = page.getByRole('button', { name: 'Download (JSON)', exact: true });
    this.downloadCopy = page.getByRole('button', {
      name: 'Download a copy of my data (JSON)',
      exact: true,
    });
    this.error = page.getByRole('main').getByRole('alert');
  }

  /** Each card's value and label, which have no accessible structure of their own. */
  async cards(): Promise<string[][]> {
    // eslint-disable-next-line playwright/no-raw-locators -- A stat card is a styled pair of spans.
    return this.page
      .locator('.stat-card')
      .evaluateAll((cards) =>
        cards.map((card) => [...card.children].map((part) => part.textContent?.trim() ?? '')),
      );
  }

  /** The page's text and the styles a person sees, read the same way from either UI. */
  async presentation() {
    // eslint-disable-next-line playwright/no-raw-locators -- Visual parity reads the page's own layout classes.
    return this.page.locator('.page-header, .panel').evaluateAll((blocks) => {
      const look = (element: Element | null) => {
        if (!element) return null;
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
          style.gap,
        ];
      };
      return blocks.map((block) => ({
        className: block.className,
        text: block.textContent?.replace(/\s+/g, ' ').trim(),
        block: look(block),
        parts: [
          ...block.querySelectorAll(
            'h1, h2, .page-sub, em, dl, dt, dd, .stat-grid, .stat-card, .stat-value, .stat-label, p.muted, .button',
          ),
        ].map((part) => [part.tagName, part.className, look(part)]),
      }));
    });
  }
}
