import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

export type SecretsView = 'secrets' | 'shared-with-me' | 'shared-by-me';

const views: readonly {
  readonly key: SecretsView;
  readonly label: string;
  readonly url: string;
}[] = [
  { key: 'secrets', label: 'All secrets', url: '/Secrets' },
  { key: 'shared-with-me', label: 'Shared with me', url: '/Secrets/SharedWithMe' },
  { key: 'shared-by-me', label: 'Shared by me', url: '/Secrets/SharedByMe' },
];

/** Navigation links shared by the Secrets list and its two Sharing views, beside the trust line. */
@Component({
  selector: 'app-secrets-tabs',
  imports: [RouterLink],
  template: `
    <div class="secrets-bar">
      <nav class="tab-nav" aria-label="Secrets views">
        @for (view of views; track view.key) {
          <a
            class="tab-link"
            [routerLink]="view.url"
            [class.active]="current() === view.key"
            [attr.aria-current]="current() === view.key ? 'page' : null"
            >{{ view.label }}</a
          >
        }
      </nav>
      <p class="trust-line">
        <svg aria-hidden="true" viewBox="0 0 24 24">
          <rect x="3" y="11" width="18" height="11" rx="2" />
          <path d="M7 11V7a5 5 0 0 1 10 0v4" />
        </svg>
        Every reveal is logged
      </p>
    </div>
  `,
})
export class SecretsTabs {
  readonly current = input.required<SecretsView>();
  protected readonly views = views;
}
