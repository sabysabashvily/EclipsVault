import { Component, input } from '@angular/core';

/** The Legacy `_EmptyState` partial: an icon chosen by key, a heading and what the page is for. */
@Component({
  selector: 'app-empty-state',
  template: `
    <div class="empty-state">
      <div class="empty-icon">
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.7"
          stroke-linecap="round"
          stroke-linejoin="round"
        >
          @switch (icon()) {
            @case ('mail') {
              <rect x="3" y="5" width="18" height="14" rx="2" />
              <path d="M4 7l8 6 8-6" />
            }
            @case ('share') {
              <circle cx="18" cy="5" r="2.6" />
              <circle cx="6" cy="12" r="2.6" />
              <circle cx="18" cy="19" r="2.6" />
              <path d="M8.3 10.7l7.4-4.4M8.3 13.3l7.4 4.4" />
            }
            @case ('clipboard') {
              <path
                d="M9 4h6a1 1 0 0 1 1 1v1h2a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h2V5a1 1 0 0 1 1-1Z"
              />
              <path d="M9 14l2 2 4-4" />
            }
            @case ('lock') {
              <rect x="4" y="10" width="16" height="10" rx="2" />
              <path d="M8 10V7a4 4 0 0 1 8 0v3" />
              <circle cx="12" cy="15" r="1.4" />
            }
            @case ('inbox') {
              <path d="M4 13h4l1.6 3h4.8L20 13h0" />
              <path d="M5 13V6a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v7" />
              <path d="M4 13v5a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-5" />
            }
          }
        </svg>
      </div>
      <h3>{{ heading() }}</h3>
      <p>{{ message() }}</p>
      <ng-content />
    </div>
  `,
})
export class EmptyState {
  readonly icon = input.required<'clipboard' | 'inbox' | 'lock' | 'share' | 'mail'>();
  readonly heading = input.required<string>();
  readonly message = input.required<string>();
}
