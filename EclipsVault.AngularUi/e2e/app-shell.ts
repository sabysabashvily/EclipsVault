import type { Page } from '@playwright/test';

export class AppShell {
  readonly yourData;
  readonly you;
  readonly administration;
  readonly myAccess;
  readonly secrets;
  readonly paletteQuery;
  readonly navigation;
  readonly youGroup;
  readonly administrationGroup;

  async signOut(displayName: string): Promise<void> {
    await this.page.getByText(displayName, { exact: true }).click();
    await this.page.getByRole('menuitem', { name: 'Sign out', exact: true }).click();
  }

  constructor(private readonly page: Page) {
    this.yourData = page.getByRole('link', { name: 'Your data', exact: true });
    // A folding group's summary has no role of its own, so it is found by its text in the navigation.
    const navigation = page.getByRole('navigation', { name: 'Main navigation' });
    this.navigation = navigation;
    this.youGroup = navigation.getByRole('group', { name: 'You', exact: true });
    this.administrationGroup = navigation.getByRole('group', {
      name: 'Administration',
      exact: true,
    });
    this.you = navigation.getByText('You', { exact: true });
    this.administration = navigation.getByText('Administration', { exact: true });
    this.myAccess = page.getByRole('link', { name: 'My access', exact: true });
    this.secrets = page.getByRole('link', { name: 'Secrets', exact: true });
    this.paletteQuery = page.getByRole('combobox');
  }
}
