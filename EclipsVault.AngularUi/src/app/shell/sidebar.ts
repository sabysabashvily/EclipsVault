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
const sections: readonly { label: string; items: readonly (AreaLink | AreaGroup)[] }[] = [
  {
    label: 'Vault',
    items: [
      link('overview', 'Overview', '/'),
      link('secrets', 'Secrets', '/Secrets'),
      link('secrets', 'New secret', '/Secrets/Create', 'new-secret'),
      link('dynamic-secrets', 'Dynamic secrets', '/DynamicSecrets'),
      {
        label: 'Sharing',
        icon: 'shared-with-me',
        children: [
          link('shared-with-me', 'Shared with me', '/Secrets/SharedWithMe'),
          link('shared-by-me', 'Shared by me', '/Secrets/SharedByMe'),
        ],
      },
    ],
  },
  {
    label: 'Account',
    items: [
      {
        label: 'Access',
        icon: 'access-group',
        children: [
          link('access', 'My access', '/Access', 'access', true),
          link('access-requests', 'Access requests', '/AccessRequests'),
        ],
      },
      {
        label: 'Security',
        icon: 'security-group',
        children: [
          link('security', 'Security checkup', '/Security', 'security', true),
          link('sign-ins', 'Sign-in history', '/SignIns', 'sign-ins', true),
          link('sessions', 'Signed-in devices', '/Sessions', 'sessions', true),
          link('activity', 'My activity', '/Activity', 'activity', true),
        ],
      },
      link('data', 'Your data', '/Data'),
    ],
  },
  {
    label: 'Administration',
    items: [
      link('users', 'Users', '/Admin/Users'),
      link('networks', 'Networks', '/Admin/Networks'),
      link('service-accounts', 'Service accounts', '/ServiceAccounts'),
      link('audit', 'Audit log', '/Audit'),
      link('notifications', 'Notifications', '/Notifications'),
      link('encryption', 'Encryption keys', '/Encryption'),
      link('license', 'License', '/License'),
    ],
  },
];

/** Maps only recognized, server-authorized Areas, preserving Legacy order and omitting empty groups. */
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
