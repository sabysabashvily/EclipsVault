import { Route, UrlSegment, UrlSegmentGroup } from '@angular/router';
import { AccessPage } from './access/access-page';
import { ActivityPage } from './activity/activity-page';
import { routes } from './app.routes';

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

  it.each(['/', '/Index', '/Accessories', '/Access/Index/extra', '/Activityfeed', '/Activity/Index/extra', '/Secrets'])(
    'leaves %s unmatched',
    (url) => {
      expect(routeFor(url)).toBeUndefined();
    },
  );
});
