import { test, expect } from './fixtures';
import { savedAdministratorCookies } from './administrator-cookies';
import { EncryptionKeys } from './encryption';
import { AppShell } from './app-shell';
import { YourData } from './data';
import { SecretsList } from './secrets-list';

test('Encryption keys says all current, offers no re-wrap, and shows how to move to a new key', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.open();
  await expect(encryption.heading).toBeVisible();
  await expect(encryption.verdict).toHaveText('All current');
  await expect(encryption.nextKeySteps).toContainText(
    'Keep the retired key loaded while the Data Protection key ring needs it.',
  );
  await expect(encryption.currentKey).toContainText(/^kek-[0-9a-f]{8}/);
  await expect(encryption.currentKey).toContainText('Wraps every new secret');
  await expect(encryption.rewrap).toHaveCount(0);
});

test('a pending item shows the verdict, the figure and the count on the button', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.reportOnePendingSecret();
  await encryption.open();
  await expect(encryption.verdict).toHaveText('Re-wrap pending');
  await expect(encryption.rewrap).toContainText('Waiting to re-wrap');
  await expect(encryption.rotate).toHaveText('Re-wrap 1 item');
  await expect(encryption.nextKeySteps).toHaveCount(0);
});

test('Re-wrap asks with the count and what it does before re-wrapping anything', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.reportOnePendingSecret();
  await encryption.open();
  await encryption.rotate.click();
  await expect(encryption.confirmation).toHaveAccessibleName('Re-wrap 1 item?');
  await expect(encryption.confirmation).toContainText(
    "Each item's data key is wrapped again by the current master key. Secret values are not decrypted or re-encrypted, and it is safe to run again.",
  );
  await expect(encryption.confirmation).toContainText('Saved to the audit log');
  expect(encryption.rotationsSent).toEqual([]);
});

test('cancelling the confirmation re-wraps nothing and returns to the Re-wrap button', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.reportOnePendingSecret();
  await encryption.open();
  await encryption.rotate.click();
  await encryption.keepKeys.click();
  await expect(encryption.confirmation).toBeHidden();
  await expect(encryption.rotate).toBeFocused();
  expect(encryption.rotationsSent).toEqual([]);
});

test('while a re-wrap runs its button says so and is disabled, then the outcome shows', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.reportOnePendingSecret();
  let release!: () => void;
  await encryption.answerRewrap(
    {
      json: {
        currentKekId: 'kek-0e2e0001',
        secretsRewrapped: 1,
        versionsRewrapped: 0,
        onMissingKeys: 0,
      },
    },
    new Promise<void>((resolve) => {
      release = resolve;
    }),
  );
  await encryption.open();
  await encryption.rotate.click();
  await encryption.continue.click();
  await expect(encryption.verdict).toHaveText('Re-wrapping');
  await expect(encryption.rotate).toHaveText('Re-wrapping…');
  await expect(encryption.rotate).toBeDisabled();
  release();
  await expect(
    page.getByRole('heading', {
      name: 'Re-wrapped 1 secret and 0 versions under the current key',
      exact: true,
    }),
  ).toBeVisible();
  await page.unrouteAll({ behavior: 'wait' });
});

test('a re-wrap already running elsewhere changes nothing and says so', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.reportOnePendingSecret();
  await encryption.answerRewrap({
    status: 409,
    contentType: 'application/problem+json',
    json: {
      code: 'rotation_running',
      title: 'Another rotation is already running. Try again when it finishes.',
    },
  });
  browserHealth.expectConsoleError({
    text: /status of 409/,
    url: /\/api\/ui\/encryption\/rotate$/,
  });
  await encryption.open();
  await encryption.rotate.click();
  await encryption.continue.click();
  await expect(
    page.getByRole('heading', { name: 'Another re-wrap is already running', exact: true }),
  ).toBeVisible();
  await expect(encryption.verdict).toHaveText('Re-wrap pending');
  await expect(encryption.rotate).toBeEnabled();
  // The page reloads the mocked figures after the refusal; let that fetch finish before the test ends.
  await page.unrouteAll({ behavior: 'wait' });
});

test('on a phone the re-wrap confirmation is a bottom sheet', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 800 });
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.reportOnePendingSecret();
  await encryption.open();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(375);
  await encryption.rotate.click();
  // The sheet slides up; once it settles it spans the width and sits on the bottom edge.
  await expect
    .poll(async () => {
      const box = await encryption.confirmation.boundingBox();
      return box && [Math.round(box.x), Math.round(box.width), Math.round(box.y + box.height)];
    })
    .toEqual([0, 375, 800]);
  await page.keyboard.press('Escape');
  await expect(encryption.confirmation).toBeHidden();
  expect(encryption.rotationsSent).toEqual([]);
});

for (const width of [300, 2000]) {
  test(`Encryption keys with an item waiting has no sideways scroll at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.context().addCookies(await savedAdministratorCookies());
    const encryption = new EncryptionKeys(page);
    await encryption.reportOnePendingSecret();
    await encryption.open();
    await expect(encryption.rotate).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}

test('the Angular sidebar opens Encryption keys', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.fromSidebar();
  await expect(encryption.heading).toBeVisible();
  await expect(page).toHaveURL(/\/Encryption$/);
});

test('the palette in the Angular shell opens Encryption keys', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.fromPalette();
  await expect(encryption.heading).toBeVisible();
});

test('the sidebar on Encryption keys returns to the Secrets list', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.open();
  await expect(encryption.heading).toBeVisible();
  await new AppShell(page).secrets.click();
  await expect(new SecretsList(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets$/);
});

test('the sidebar on Encryption keys opens Your data in the Angular UI', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.open();
  await expect(encryption.heading).toBeVisible();
  // Your data is in the You group, which an administration page leaves folded.
  const shell = new AppShell(page);
  await shell.you.click();
  await shell.yourData.click();
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Data$/);
});

for (const theme of ['dark', 'light'] as const) {
  test(`the confirmation opens, cycles and closes by keyboard in ${theme} appearance`, async ({
    page,
  }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const encryption = new EncryptionKeys(page);
    await encryption.reportOnePendingSecret();
    await encryption.open();
    await encryption.useTheme(theme);
    await encryption.rotate.focus();
    await page.keyboard.press('Enter');
    await expect(encryption.keepKeys).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(encryption.continue).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(encryption.keepKeys).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(encryption.confirmation).toBeHidden();
    await expect(encryption.rotate).toBeFocused();
    expect(encryption.rotationsSent).toEqual([]);
  });
}
