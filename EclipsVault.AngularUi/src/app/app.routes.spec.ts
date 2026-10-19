import { Route, UrlSegment, UrlSegmentGroup } from '@angular/router';
import { AccessPage } from './access/access-page';
import { ActivityPage } from './activity/activity-page';
import { routes } from './app.routes';
import { SignInsPage } from './sign-ins/sign-ins-page';

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

  it.each([
    '/', '/Index', '/Accessories', '/Access/Index/extra', '/Activityfeed', '/Activity/Index/extra',
    '/SignInsLog', '/SignIns/Index/extra', '/Secrets',
  ])(
    'leaves %s unmatched',
    (url) => {
      expect(routeFor(url)).toBeUndefined();
    },
  );
});
