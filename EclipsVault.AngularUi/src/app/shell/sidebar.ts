export interface SidebarLink {
  readonly label: string;
  readonly icon: string;
  readonly url: string;
  readonly angular: boolean;
}

export interface SidebarGroup {
  readonly label: string;
  readonly icon: string;
  readonly children: readonly SidebarLink[];
}

export interface SidebarSection {
  readonly label: string;
  readonly items: readonly (SidebarLink | SidebarGroup)[];
}

interface AreaLink extends SidebarLink {
  readonly area: string;
}

interface AreaGroup {
  readonly label: string;
  readonly icon: string;
  readonly children: readonly AreaLink[];
}

const link = (
  area: string,
  label: string,
  url: string,
  icon = area,
  angular = false,
): AreaLink => ({ area, label, url, icon, angular });

// Presentation only: the server has already authorized each Area. Clearance is never an input.
// Profile is offered in the account menu. The command palette reads the rendered sidebar links.
// The places people visit come first; personal and administrative pages fold into the You and
// Administration groups, which name themselves, so only the first section carries a label.
const sections: readonly { label: string; items: readonly (AreaLink | AreaGroup)[] }[] = [
  {
    label: 'Vault',
    items: [
      link('overview', 'Overview', '/', 'overview', true),
      link('secrets', 'Secrets', '/Secrets', 'secrets', true),
      link('dynamic-secrets', 'Dynamic secrets', '/DynamicSecrets', 'dynamic-secrets', true),
      // Sharing becomes tabs on the Secrets card once the Secrets list is an Angular page; until
      // then its two pages keep a group of their own here.
      {
        label: 'Sharing',
        icon: 'shared-with-me',
        children: [
          link('shared-with-me', 'Shared with me', '/Secrets/SharedWithMe', 'shared-with-me', true),
          link('shared-by-me', 'Shared by me', '/Secrets/SharedByMe', 'shared-by-me', true),
        ],
      },
      link('access-requests', 'Access requests', '/AccessRequests', 'access-requests', true),
    ],
  },
  {
    label: '',
    items: [
      {
        label: 'You',
        icon: 'you',
        children: [
          link('access', 'My access', '/Access', 'access', true),
          link('security', 'Security checkup', '/Security', 'security', true),
          link('sign-ins', 'Sign-in history', '/SignIns', 'sign-ins', true),
          link('sessions', 'Signed-in devices', '/Sessions', 'sessions', true),
          link('activity', 'My activity', '/Activity', 'activity', true),
          link('data', 'Your data', '/Data', 'data', true),
        ],
      },
      {
        label: 'Administration',
        icon: 'administration',
        children: [
          link('users', 'Users', '/Admin/Users', 'users', true),
          link(
            'service-accounts',
            'Service accounts',
            '/ServiceAccounts',
            'service-accounts',
            true,
          ),
          link('networks', 'Networks', '/Admin/Networks', 'networks', true),
          link('encryption', 'Encryption keys', '/Encryption', 'encryption', true),
          link('audit', 'Audit log', '/Audit', 'audit', true),
          link('notifications', 'Notifications', '/Notifications', 'notifications', true),
          link('license', 'License', '/License', 'license', true),
        ],
      },
    ],
  },
];

/**
 * The palette offers creation as a shortcut; sidebar links name destinations.
 */
const shortcuts: readonly AreaLink[] = [link('secrets', 'New secret', '/Secrets/Create')];

/** Maps only recognized, server-authorized Areas, in sidebar order, omitting empty groups and sections. */
export function sidebarFor(areas: readonly string[]): readonly SidebarSection[] {
  const allowed = new Set(areas);
  const visible = (entry: AreaLink): SidebarLink[] => {
    const { area, ...presentation } = entry;
    return allowed.has(area) ? [presentation] : [];
  };
  return sections.flatMap((section) => {
    const items = section.items.flatMap((item): (SidebarLink | SidebarGroup)[] => {
      if (!('children' in item)) return visible(item);
      const children = item.children.flatMap(visible);
      return children.length ? [{ label: item.label, icon: item.icon, children }] : [];
    });
    return items.length ? [{ label: section.label, items }] : [];
  });
}

/** The palette shortcuts the server-authorized Areas allow, in order. */
export function shortcutsFor(areas: readonly string[]): readonly SidebarLink[] {
  const allowed = new Set(areas);
  return shortcuts.flatMap((entry) => {
    const { area, ...shortcut } = entry;
    return allowed.has(area) ? [shortcut] : [];
  });
}
