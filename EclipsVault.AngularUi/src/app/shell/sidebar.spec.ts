import { sidebarFor } from './sidebar';

describe('sidebar mapping', () => {
  it('maps the server Areas to the Legacy groups, labels, icons and URLs in Legacy order', () => {
    const sections = sidebarFor([
      'data', 'activity', 'sessions', 'sign-ins', 'security', 'access-requests', 'access',
      'shared-by-me', 'shared-with-me', 'dynamic-secrets', 'secrets', 'overview', 'profile',
    ]);

    expect(sections).toEqual([
      { label: 'Vault', items: [
        { label: 'Overview', icon: 'overview', url: '/', angular: false },
        { label: 'Secrets', icon: 'secrets', url: '/Secrets', angular: false },
        { label: 'New secret', icon: 'new-secret', url: '/Secrets/Create', angular: false },
        { label: 'Dynamic secrets', icon: 'dynamic-secrets', url: '/DynamicSecrets', angular: false },
        { label: 'Sharing', icon: 'shared-with-me', children: [
          { label: 'Shared with me', icon: 'shared-with-me', url: '/Secrets/SharedWithMe', angular: false },
          { label: 'Shared by me', icon: 'shared-by-me', url: '/Secrets/SharedByMe', angular: false },
        ] },
      ] },
      { label: 'Account', items: [
        { label: 'Access', icon: 'access-group', children: [
          { label: 'My access', icon: 'access', url: '/Access', angular: true },
          { label: 'Access requests', icon: 'access-requests', url: '/AccessRequests', angular: false },
        ] },
        { label: 'Security', icon: 'security-group', children: [
          { label: 'Security checkup', icon: 'security', url: '/Security', angular: false },
          { label: 'Sign-in history', icon: 'sign-ins', url: '/SignIns', angular: false },
          { label: 'Signed-in devices', icon: 'sessions', url: '/Sessions', angular: false },
          { label: 'My activity', icon: 'activity', url: '/Activity', angular: true },
        ] },
        { label: 'Your data', icon: 'data', url: '/Data', angular: false },
      ] },
    ]);
  });

  it('maps every administrator Area in Legacy order', () => {
    expect(sidebarFor(['license', 'encryption', 'notifications', 'audit', 'service-accounts', 'networks', 'users']))
      .toEqual([{ label: 'Administration', items: [
        { label: 'Users', icon: 'users', url: '/Admin/Users', angular: false },
        { label: 'Networks', icon: 'networks', url: '/Admin/Networks', angular: false },
        { label: 'Service accounts', icon: 'service-accounts', url: '/ServiceAccounts', angular: false },
        { label: 'Audit log', icon: 'audit', url: '/Audit', angular: false },
        { label: 'Notifications', icon: 'notifications', url: '/Notifications', angular: false },
        { label: 'Encryption keys', icon: 'encryption', url: '/Encryption', angular: false },
        { label: 'License', icon: 'license', url: '/License', angular: false },
      ] }]);
  });

  it('omits empty groups and unknown Areas and never duplicates an offered entry', () => {
    expect(sidebarFor([])).toEqual([]);
    expect(sidebarFor(['unrecognized', 'profile'])).toEqual([]);
    expect(sidebarFor(['access', 'access'])).toEqual([{ label: 'Account', items: [
      { label: 'Access', icon: 'access-group', children: [
        { label: 'My access', icon: 'access', url: '/Access', angular: true },
      ] },
    ] }]);
  });
});
