import { Route, UrlSegment, UrlSegmentGroup } from '@angular/router';
import { ProfilePage } from './profile/profile-page';
import { ChangePasswordPage } from './profile/change-password-page';
import { AccessPage } from './access/access-page';
import { ActivityPage } from './activity/activity-page';
import { routes } from './app.routes';
import { SignInsPage } from './sign-ins/sign-ins-page';
import { SecurityPage } from './security/security-page';
import { SessionsPage } from './sessions/sessions-page';
import { DataPage } from './data/data-page';
import { SharedByMePage } from './shared-by-me/shared-by-me-page';
import { SharedWithMePage } from './shared-with-me/shared-with-me-page';

/** The route the router picks for `url`: the first whose matcher accepts its segments. */
function routeFor(url: string): Route | undefined {
  const segments = url
    .split('/')
    .filter((path) => path !== '')
    .map((path) => new UrlSegment(path, {}));
  const group = new UrlSegmentGroup(segments, {});
  return routes.find((route) => route.matcher?.(segments, group, route));
}

describe('routes', () => {
  it.each(['/Profile', '/Profile/Index', '/pRoFiLe', '/PROFILE/index'])(
    'opens Profile at %s',
    async (url) => {
      const route = routeFor(url);
      expect(route?.title).toBe('Profile — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(ProfilePage);
    },
  );
  it.each(['/Profile/ChangePassword', '/pRoFiLe/cHaNgEpAsSwOrD'])(
    'opens Change password at %s',
    async (url) => {
      const route = routeFor(url);
      expect(route?.title).toBe('Change password — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(ChangePasswordPage);
    },
  );
  it.each(['/Profile/RecoveryCodes', '/PROFILE/recoverycodes'])(
    'returns a direct recovery-code visit at %s to Profile without plaintext',
    (url) => {
      expect(routeFor(url)?.redirectTo).toBe('/Profile?recoveryCodes=display-once');
    },
  );
  it.each(['/Secrets/SharedByMe', '/secrets/sharedbyme', '/sEcReTs/sHaReDbYmE'])(
    'opens Shared by me at %s',
    async (url) => {
      const route = routeFor(url);
      expect(route?.title).toBe('Shared by me — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(SharedByMePage);
    },
  );
  it.each(['/Secrets/SharedWithMe', '/secrets/sharedwithme', '/sEcReTs/sHaReDwItHmE'])(
    'opens Shared with me at %s',
    async (url) => {
      const route = routeFor(url);
      expect(route?.title).toBe('Shared with me — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(SharedWithMePage);
    },
  );
  it.each(['/Sessions', '/Sessions/Index', '/sessions', '/SESSIONS/index'])(
    'opens Signed-in devices at %s',
    async (url) => {
      const route = routeFor(url);
      expect(route?.title).toBe('Signed-in devices — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(SessionsPage);
    },
  );
  it.each(['/Data', '/Data/Index', '/data', '/DATA/index'])(
    'opens Your data at %s',
    async (url) => {
      const route = routeFor(url);
      expect(route?.title).toBe('Your data — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(DataPage);
    },
  );
  it.each(['/Access', '/Access/Index', '/access', '/ACCESS/index'])(
    'opens My access at %s',
    async (url) => {
      const route = routeFor(url);

      expect(route).toBeDefined();
      expect(await route?.loadComponent?.()).toBe(AccessPage);
    },
  );

  it.each(['/Activity', '/Activity/Index', '/activity', '/ACTIVITY/index'])(
    'opens My activity at %s',
    async (url) => {
      const route = routeFor(url);

      expect(route).toBeDefined();
      expect(route?.title).toBe('My activity — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(ActivityPage);
    },
  );

  it.each(['/SignIns', '/SignIns/Index', '/signins', '/SIGNINS/index'])(
    'opens Sign-in history at %s',
    async (url) => {
      const route = routeFor(url);

      expect(route).toBeDefined();
      expect(route?.title).toBe('Sign-in history — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(SignInsPage);
    },
  );

  it.each(['/Security', '/Security/Index', '/security', '/SECURITY/index'])(
    'opens Security checkup at %s',
    async (url) => {
      const route = routeFor(url);

      expect(route).toBeDefined();
      expect(route?.title).toBe('Security checkup — EclipsVault');
      expect(await route?.loadComponent?.()).toBe(SecurityPage);
    },
  );

  it.each([
    '/',
    '/Index',
    '/Accessories',
    '/Access/Index/extra',
    '/Activityfeed',
    '/Activity/Index/extra',
    '/SignInsLog',
    '/SignIns/Index/extra',
    '/SecurityLog',
    '/Security/Index/extra',
    '/SessionsLog',
    '/Sessions/Index/extra',
    '/DataExport',
    '/Data/Index/extra',
    '/Data/Download',
    '/Secrets',
    '/Secrets/Details',
    '/Secrets/SharedWithMe/extra',
    '/Secrets/SharedByMe/extra',
  ])('leaves %s unmatched', (url) => {
    expect(routeFor(url)).toBeUndefined();
  });
});
