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
          { label: 'Overview', icon: 'overview', url: '/' },
          { label: 'Secrets', icon: 'secrets', url: '/Secrets' },
          {
            label: 'Dynamic secrets',
            icon: 'dynamic-secrets',
            url: '/DynamicSecrets',
          },
          {
            label: 'Access requests',
            icon: 'access-requests',
            url: '/AccessRequests',
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
              { label: 'My access', icon: 'access', url: '/Access' },
              { label: 'Security checkup', icon: 'security', url: '/Security' },
              { label: 'Sign-in history', icon: 'sign-ins', url: '/SignIns' },
              { label: 'Signed-in devices', icon: 'sessions', url: '/Sessions' },
              { label: 'My activity', icon: 'activity', url: '/Activity' },
              { label: 'Your data', icon: 'data', url: '/Data' },
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
              { label: 'Users', icon: 'users', url: '/Admin/Users' },
              {
                label: 'Service accounts',
                icon: 'service-accounts',
                url: '/ServiceAccounts',
              },
              { label: 'Networks', icon: 'networks', url: '/Admin/Networks' },
              { label: 'Encryption keys', icon: 'encryption', url: '/Encryption' },
              { label: 'Audit log', icon: 'audit', url: '/Audit' },
              {
                label: 'Notifications',
                icon: 'notifications',
                url: '/Notifications',
              },
              { label: 'License', icon: 'license', url: '/License' },
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
            children: [{ label: 'My access', icon: 'access', url: '/Access' }],
          },
        ],
      },
    ]);
  });
});

describe('palette shortcuts', () => {
  it('offers New secret and the Sharing tabs only to accounts whose Areas allow them', () => {
    expect(shortcutsFor(['secrets', 'shared-with-me', 'shared-by-me'])).toEqual([
      { label: 'New secret', icon: 'secrets', url: '/Secrets/Create' },
      {
        label: 'Shared with me',
        icon: 'shared-with-me',
        url: '/Secrets/SharedWithMe',
      },
      { label: 'Shared by me', icon: 'shared-by-me', url: '/Secrets/SharedByMe' },
    ]);
    expect(shortcutsFor(['secrets'])).toEqual([
      { label: 'New secret', icon: 'secrets', url: '/Secrets/Create' },
    ]);
    expect(shortcutsFor(['access'])).toEqual([]);
  });
});
