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
          }
        </svg>
      </div>
      <h3>{{ heading() }}</h3>
      <p>{{ message() }}</p>
    </div>
  `,
})
export class EmptyState {
  readonly icon = input.required<'clipboard' | 'lock'>();
  readonly heading = input.required<string>();
  readonly message = input.required<string>();
}
