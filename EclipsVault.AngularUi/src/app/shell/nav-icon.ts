import { Component, input } from '@angular/core';

/** The Legacy sidebar's SVG geometry, rendered as template nodes without HTML injection. */
@Component({
  selector: 'app-nav-icon',
  template: `
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.8"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      @switch (icon()) {
        @case ('overview') {
          <rect x="3" y="3" width="7" height="9" rx="1.5" />
          <rect x="14" y="3" width="7" height="5" rx="1.5" />
          <rect x="14" y="12" width="7" height="9" rx="1.5" />
          <rect x="3" y="16" width="7" height="5" rx="1.5" />
        }
        @case ('secrets') {
          <rect x="4" y="10" width="16" height="10" rx="2" />
          <path d="M8 10V7a4 4 0 0 1 8 0v3" />
          <circle cx="12" cy="15" r="1.4" />
        }
        @case ('new-secret') {
          <circle cx="12" cy="12" r="9" />
          <path d="M12 8v8M8 12h8" />
        }
        @case ('dynamic-secrets') {
          <path d="M13 2 4 14h7l-1 8 9-12h-7l1-8z" />
        }
        @case ('shared-with-me') {
          <circle cx="18" cy="5" r="2.6" />
          <circle cx="6" cy="12" r="2.6" />
          <circle cx="18" cy="19" r="2.6" />
          <path d="M8.3 10.7l7.4-4.4M8.3 13.3l7.4 4.4" />
        }
        @case ('shared-by-me') {
          <circle cx="9" cy="8" r="3.2" />
          <path d="M3.5 20c.6-3.4 2.8-5 5.5-5 1.3 0 2.5.4 3.4 1" />
          <path d="M16 15h6M19 12l3 3-3 3" />
        }
        @case ('access') {
          <rect x="4" y="10" width="16" height="10" rx="2" />
          <path d="M8 10V7a4 4 0 0 1 8 0v3" />
          <path d="m10.5 15 1.2 1.2 2.3-2.4" />
        }
        @case ('access-requests') {
          <path
            d="M9 4h6a1 1 0 0 1 1 1v1h2a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h2V5a1 1 0 0 1 1-1Z"
          />
          <path d="M9 14l2 2 4-4" />
        }
        @case ('security') {
          <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z" />
          <path d="M9 12l2 2 4-4" />
        }
        @case ('sign-ins') {
          <path d="M15 3h3a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-3" />
          <path d="M8 7l5 5-5 5M13 12H3" />
        }
        @case ('sessions') {
          <rect x="2" y="4" width="14" height="10" rx="1.6" />
          <path d="M6 18h6M9 14v4" />
          <rect x="17" y="9" width="5" height="11" rx="1.4" />
        }
        @case ('activity') {
          <path d="M3 12h4l2.5-7 4 14 2.5-7H21" />
        }
        @case ('data') {
          <path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z" />
          <path d="M14 3v5h5" />
          <path d="M12 12v5M9.5 14.5 12 17l2.5-2.5" />
        }
        @case ('users') {
          <circle cx="9" cy="8" r="3.2" />
          <path d="M3.5 20c.6-3.4 2.8-5 5.5-5s4.9 1.6 5.5 5" />
          <circle cx="17" cy="9" r="2.4" />
          <path d="M15.5 14.6c2.6.2 4.4 1.7 5 4.9" />
        }
        @case ('networks') {
          <circle cx="12" cy="12" r="9" />
          <path d="M3 12h18M12 3c2.7 2.6 4 5.7 4 9s-1.3 6.4-4 9c-2.7-2.6-4-5.7-4-9s1.3-6.4 4-9Z" />
        }
        @case ('service-accounts') {
          <rect x="3" y="4" width="18" height="6" rx="1.5" />
          <rect x="3" y="14" width="18" height="6" rx="1.5" />
          <path d="M7 7h.01M7 17h.01" />
        }
        @case ('audit') {
          <path d="M3 12h4l3-8 4 16 3-8h4" />
        }
        @case ('notifications') {
          <path d="M4 7l8 6 8-6" />
          <rect x="3" y="5" width="18" height="14" rx="2" />
        }
        @case ('encryption') {
          <circle cx="8" cy="15" r="4" />
          <path d="M10.85 12.15 19 4M18 5l2 2M15 8l2 2" />
        }
        @case ('license') {
          <rect x="3" y="4" width="18" height="16" rx="2" />
          <path d="M7 9h6M7 13h4" />
          <circle cx="16.3" cy="13.6" r="2.2" />
          <path d="m14.9 15.2-.6 2.3 2-1 2 1-.6-2.3" />
        }
        @case ('access-group') {
          <circle cx="7" cy="14" r="3.5" />
          <path d="M9.5 11.5 20 1M17 4l2 2M14 7l2 2" />
        }
        @case ('security-group') {
          <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z" />
        }
      }
    </svg>
  `,
})
export class NavIcon {
  readonly icon = input.required<string>();
}
