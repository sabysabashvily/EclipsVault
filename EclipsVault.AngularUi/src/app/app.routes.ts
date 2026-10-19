import { Routes, UrlMatcher } from '@angular/router';

// Every path here must also be listed in the server's route table (AngularUiRoutes.cs), which is
// what serves this app at that URL. Paths keep the Legacy UI's URLs so bookmarks keep working.

/**
 * Matches `path` ignoring case, as the server and the Legacy UI's routes do, so a bookmark to
 * `/access` opens the page instead of an unmatched route.
 */
function legacyPath(path: string): UrlMatcher {
  const expected = path.toLowerCase().split('/');
  return (segments) =>
    segments.length === expected.length &&
    segments.every((segment, i) => segment.path.toLowerCase() === expected[i])
      ? { consumed: segments }
      : null;
}

const access = () => import('./access/access-page').then((m) => m.AccessPage);
const activity = () => import('./activity/activity-page').then((m) => m.ActivityPage);
const signIns = () => import('./sign-ins/sign-ins-page').then((m) => m.SignInsPage);

export const routes: Routes = [
  { matcher: legacyPath('Access'), loadComponent: access, title: 'My access — EclipsVault' },
  { matcher: legacyPath('Access/Index'), loadComponent: access, title: 'My access — EclipsVault' },
  { matcher: legacyPath('Activity'), loadComponent: activity, title: 'My activity — EclipsVault' },
  { matcher: legacyPath('Activity/Index'), loadComponent: activity, title: 'My activity — EclipsVault' },
  { matcher: legacyPath('SignIns'), loadComponent: signIns, title: 'Sign-in history — EclipsVault' },
  { matcher: legacyPath('SignIns/Index'), loadComponent: signIns, title: 'Sign-in history — EclipsVault' },
];
