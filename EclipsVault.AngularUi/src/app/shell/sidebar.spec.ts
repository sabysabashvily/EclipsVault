import { shortcutsFor, sidebarFor } from './sidebar';

describe('sidebar mapping', () => {
  it('maps a standard account to the places it visits, then the You group', () => {
    const sections = sidebarFor([
      'data',
      'activity',
      'sessions',
      'sign-ins',
      'security',
      'access-requests',
      'access',
      'shared-by-me',
      'shared-with-me',
      'dynamic-secrets',
      'secrets',
      'overview',
      'profile',
    ]);

    expect(sections).toEqual([
      {
        label: 'Vault',
        items: [
          { label: 'Overview', icon: 'overview', url: '/', angular: true },
          { label: 'Secrets', icon: 'secrets', url: '/Secrets', angular: true },
          {
            label: 'Dynamic secrets',
            icon: 'dynamic-secrets',
            url: '/DynamicSecrets',
            angular: true,
          },
          {
            label: 'Sharing',
            icon: 'shared-with-me',
            children: [
              {
                label: 'Shared with me',
                icon: 'shared-with-me',
                url: '/Secrets/SharedWithMe',
                angular: true,
              },
              {
                label: 'Shared by me',
                icon: 'shared-by-me',
                url: '/Secrets/SharedByMe',
                angular: true,
              },
            ],
          },
          {
            label: 'Access requests',
            icon: 'access-requests',
            url: '/AccessRequests',
            angular: true,
          },
        ],
      },
      {
        label: '',
        items: [
          {
            label: 'You',
            icon: 'you',
            children: [
              { label: 'My access', icon: 'access', url: '/Access', angular: true },
              { label: 'Security checkup', icon: 'security', url: '/Security', angular: true },
              { label: 'Sign-in history', icon: 'sign-ins', url: '/SignIns', angular: true },
              { label: 'Signed-in devices', icon: 'sessions', url: '/Sessions', angular: true },
              { label: 'My activity', icon: 'activity', url: '/Activity', angular: true },
              { label: 'Your data', icon: 'data', url: '/Data', angular: true },
            ],
          },
        ],
      },
    ]);
  });

  it('folds every administrator Area into the Administration group', () => {
    expect(
      sidebarFor([
        'license',
        'encryption',
        'notifications',
        'audit',
        'service-accounts',
        'networks',
        'users',
      ]),
    ).toEqual([
      {
        label: '',
        items: [
          {
            label: 'Administration',
            icon: 'administration',
            children: [
              { label: 'Users', icon: 'users', url: '/Admin/Users', angular: true },
              {
                label: 'Service accounts',
                icon: 'service-accounts',
                url: '/ServiceAccounts',
                angular: true,
              },
              { label: 'Networks', icon: 'networks', url: '/Admin/Networks', angular: true },
              { label: 'Encryption keys', icon: 'encryption', url: '/Encryption', angular: true },
              { label: 'Audit log', icon: 'audit', url: '/Audit', angular: true },
              {
                label: 'Notifications',
                icon: 'notifications',
                url: '/Notifications',
                angular: true,
              },
              { label: 'License', icon: 'license', url: '/License', angular: true },
            ],
          },
        ],
      },
    ]);
  });

  it('omits empty groups and unknown Areas and never duplicates an offered entry', () => {
    expect(sidebarFor([])).toEqual([]);
    expect(sidebarFor(['unrecognized', 'profile'])).toEqual([]);
    expect(sidebarFor(['access', 'access'])).toEqual([
      {
        label: '',
        items: [
          {
            label: 'You',
            icon: 'you',
            children: [{ label: 'My access', icon: 'access', url: '/Access', angular: true }],
          },
        ],
      },
    ]);
  });
});

describe('palette shortcuts', () => {
  it('offers New secret only to an account that may open Secrets', () => {
    expect(shortcutsFor(['secrets'])).toEqual([
      { label: 'New secret', icon: 'secrets', url: '/Secrets/Create', angular: false },
    ]);
    expect(shortcutsFor(['access'])).toEqual([]);
  });
});
