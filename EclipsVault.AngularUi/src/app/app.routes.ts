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
const security = () => import('./security/security-page').then((m) => m.SecurityPage);
const sessions = () => import('./sessions/sessions-page').then((m) => m.SessionsPage);
const data = () => import('./data/data-page').then((m) => m.DataPage);
const sharedByMe = () => import('./shared-by-me/shared-by-me-page').then((m) => m.SharedByMePage);

export const routes: Routes = [
  {
    matcher: legacyPath('Secrets/SharedByMe'),
    loadComponent: sharedByMe,
    title: 'Shared by me — EclipsVault',
  },
  {
    matcher: legacyPath('Sessions'),
    loadComponent: sessions,
    title: 'Signed-in devices — EclipsVault',
  },
  {
    matcher: legacyPath('Sessions/Index'),
    loadComponent: sessions,
    title: 'Signed-in devices — EclipsVault',
  },
  { matcher: legacyPath('Data'), loadComponent: data, title: 'Your data — EclipsVault' },
  { matcher: legacyPath('Data/Index'), loadComponent: data, title: 'Your data — EclipsVault' },
  { matcher: legacyPath('Access'), loadComponent: access, title: 'My access — EclipsVault' },
  { matcher: legacyPath('Access/Index'), loadComponent: access, title: 'My access — EclipsVault' },
  { matcher: legacyPath('Activity'), loadComponent: activity, title: 'My activity — EclipsVault' },
  {
    matcher: legacyPath('Activity/Index'),
    loadComponent: activity,
    title: 'My activity — EclipsVault',
  },
  {
    matcher: legacyPath('SignIns'),
    loadComponent: signIns,
    title: 'Sign-in history — EclipsVault',
  },
  {
    matcher: legacyPath('SignIns/Index'),
    loadComponent: signIns,
    title: 'Sign-in history — EclipsVault',
  },
  {
    matcher: legacyPath('Security'),
    loadComponent: security,
    title: 'Security checkup — EclipsVault',
  },
  {
    matcher: legacyPath('Security/Index'),
    loadComponent: security,
    title: 'Security checkup — EclipsVault',
  },
];
