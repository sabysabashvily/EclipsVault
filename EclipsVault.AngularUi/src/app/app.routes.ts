import { Routes, UrlMatcher } from '@angular/router';

// Every path here must also be listed in the server's route table (AngularUiRoutes.cs), which is
// what serves this app at that URL; the Overview's are served by HomeController.Index instead. Paths
// keep the Legacy UI's URLs so bookmarks keep working.

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

function detailsPath(controller: string, action: string): UrlMatcher {
  return (segments) =>
    segments.length === 3 &&
    segments[0]?.path.toLowerCase() === controller.toLowerCase() &&
    segments[1]?.path.toLowerCase() === action.toLowerCase() &&
    /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(segments[2]?.path ?? '')
      ? { consumed: segments, posParams: { id: segments[2] } }
      : null;
}

const access = () => import('./access/access-page').then((m) => m.AccessPage);
const activity = () => import('./activity/activity-page').then((m) => m.ActivityPage);
const signIns = () => import('./sign-ins/sign-ins-page').then((m) => m.SignInsPage);
const security = () => import('./security/security-page').then((m) => m.SecurityPage);
const sessions = () => import('./sessions/sessions-page').then((m) => m.SessionsPage);
const data = () => import('./data/data-page').then((m) => m.DataPage);
const sharedByMe = () => import('./shared-by-me/shared-by-me-page').then((m) => m.SharedByMePage);
const sharedWithMe = () =>
  import('./shared-with-me/shared-with-me-page').then((m) => m.SharedWithMePage);
const accessRequests = () =>
  import('./access-requests/access-requests-page').then((m) => m.AccessRequestsPage);

const profile = () => import('./profile/profile-page').then((m) => m.ProfilePage);
const password = () => import('./profile/change-password-page').then((m) => m.ChangePasswordPage);

const serviceAccounts = () =>
  import('./service-accounts/service-accounts-page').then((m) => m.ServiceAccountsPage);

const notifications = () =>
  import('./notifications/notifications-page').then((m) => m.NotificationsPage);
const license = () => import('./license/license-page').then((m) => m.LicensePage);
const encryption = () => import('./encryption/encryption-page').then((m) => m.EncryptionPage);
const audit = () => import('./audit/audit-page').then((m) => m.AuditPage);
const overview = () => import('./overview/overview-page').then((m) => m.OverviewPage);
const secrets = () => import('./secrets/secrets-page').then((m) => m.SecretsPage);

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: overview, title: 'Overview — EclipsVault' },
  { matcher: legacyPath('Home'), loadComponent: overview, title: 'Overview — EclipsVault' },
  { matcher: legacyPath('Home/Index'), loadComponent: overview, title: 'Overview — EclipsVault' },
  {
    matcher: legacyPath('Notifications'),
    loadComponent: notifications,
    title: 'Notifications — EclipsVault',
  },
  {
    matcher: legacyPath('Notifications/Index'),
    loadComponent: notifications,
    title: 'Notifications — EclipsVault',
  },
  { matcher: legacyPath('License'), loadComponent: license, title: 'License — EclipsVault' },
  { matcher: legacyPath('License/Index'), loadComponent: license, title: 'License — EclipsVault' },
  { matcher: legacyPath('Audit'), loadComponent: audit, title: 'Audit log — EclipsVault' },
  { matcher: legacyPath('Audit/Index'), loadComponent: audit, title: 'Audit log — EclipsVault' },
  {
    matcher: legacyPath('Encryption'),
    loadComponent: encryption,
    title: 'Encryption keys — EclipsVault',
  },
  {
    matcher: legacyPath('Encryption/Index'),
    loadComponent: encryption,
    title: 'Encryption keys — EclipsVault',
  },
  {
    matcher: legacyPath('ServiceAccounts'),
    loadComponent: serviceAccounts,
    title: 'Service accounts — EclipsVault',
  },
  {
    matcher: legacyPath('ServiceAccounts/Index'),
    loadComponent: serviceAccounts,
    title: 'Service accounts — EclipsVault',
  },
  {
    matcher: legacyPath('ServiceAccounts/Create'),
    loadComponent: () =>
      import('./service-accounts/new-service-account-page').then((m) => m.NewServiceAccountPage),
    title: 'New service account — EclipsVault',
  },
  {
    matcher: detailsPath('ServiceAccounts', 'Details'),
    loadComponent: () =>
      import('./service-accounts/service-account-page').then((m) => m.ServiceAccountPage),
  },
  {
    matcher: legacyPath('Admin/CreateUser'),
    loadComponent: () => import('./users/new-user-page').then((m) => m.NewUserPage),
    title: 'New user — EclipsVault',
  },
  {
    matcher: detailsPath('Admin', 'EditUser'),
    loadComponent: () => import('./users/edit-user-page').then((m) => m.EditUserPage),
  },
  {
    matcher: legacyPath('Admin/Users'),
    loadComponent: () => import('./users/users-page').then((m) => m.UsersPage),
    title: 'Users — EclipsVault',
  },
  {
    matcher: legacyPath('Admin/Networks'),
    loadComponent: () => import('./networks/networks-page').then((m) => m.NetworksPage),
    title: 'Networks — EclipsVault',
  },
  { matcher: legacyPath('Profile'), loadComponent: profile, title: 'Profile — EclipsVault' },
  { matcher: legacyPath('Profile/Index'), loadComponent: profile, title: 'Profile — EclipsVault' },
  {
    matcher: legacyPath('Profile/ChangePassword'),
    loadComponent: password,
    title: 'Change password — EclipsVault',
  },
  {
    matcher: legacyPath('Profile/RecoveryCodes'),
    redirectTo: '/Profile?recoveryCodes=display-once',
  },
  { matcher: legacyPath('Secrets'), loadComponent: secrets, title: 'Secrets — EclipsVault' },
  { matcher: legacyPath('Secrets/Index'), loadComponent: secrets, title: 'Secrets — EclipsVault' },
  {
    matcher: legacyPath('Secrets/Create'),
    loadComponent: () => import('./secrets/new-secret-page').then((m) => m.NewSecretPage),
    title: 'New secret — EclipsVault',
  },
  {
    matcher: detailsPath('Secrets', 'Details'),
    loadComponent: () => import('./secrets/secret-details-page').then((m) => m.SecretDetailsPage),
  },
  {
    matcher: legacyPath('Secrets/SharedByMe'),
    loadComponent: sharedByMe,
    title: 'Shared by me — EclipsVault',
  },
  {
    matcher: legacyPath('Secrets/SharedWithMe'),
    loadComponent: sharedWithMe,
    title: 'Shared with me — EclipsVault',
  },
  {
    matcher: legacyPath('AccessRequests'),
    loadComponent: accessRequests,
    title: 'Access requests — EclipsVault',
  },
  {
    matcher: legacyPath('AccessRequests/Index'),
    loadComponent: accessRequests,
    title: 'Access requests — EclipsVault',
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
