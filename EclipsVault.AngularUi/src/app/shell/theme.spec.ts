import { currentTheme, switchTheme } from './theme';

describe('theme', () => {
  function page(theme?: string) {
    const root = document.createElement('html');
    if (theme) root.dataset['theme'] = theme;
    return { documentElement: root, cookie: '' };
  }

  it.each([
    ['light', 'light'],
    ['dark', 'dark'],
    [undefined, 'dark'],
  ])(
    'reads %s on the document as %s, defaulting to dark as the Legacy UI does',
    (applied, theme) => {
      expect(currentTheme(page(applied))).toBe(theme);
    },
  );

  it.each([
    ['dark', 'light'],
    ['light', 'dark'],
  ])('switches from %s to %s on the page and in the cookie the Legacy UI reads', (from, to) => {
    const target = page(from);

    expect(switchTheme(target)).toBe(to);

    expect(target.documentElement.dataset['theme']).toBe(to);
    expect(target.cookie).toBe(
      `EclipsVault.Theme=${to}; path=/; max-age=31536000; SameSite=Strict; Secure`,
    );
  });
});
