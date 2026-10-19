import { HttpClient, HttpResponse } from '@angular/common/http';
import {
  afterNextRender,
  Component,
  computed,
  DOCUMENT,
  ElementRef,
  inject,
  InjectionToken,
  Injector,
  input,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { catchError, debounce, map, of, Subject, switchMap, timer } from 'rxjs';

/** Where a chosen Legacy UI page is opened: the browser's own location, replaceable in tests. */
export const PAGE_LOCATION = new InjectionToken<Pick<Location, 'assign'>>('PAGE_LOCATION', {
  factory: () => inject(DOCUMENT).location,
});

export interface PaletteCommand {
  readonly kind: 'Go to' | 'Secret';
  readonly label: string;
  readonly url: string;
  /** The Angular UI owns the page, so the router opens it without a page load. */
  readonly angular: boolean;
  readonly meta?: string;
  readonly sensitivity?: number;
  readonly sensitivityName?: string;
}

/** The Legacy UI's search answers nothing shorter, and the palette does not ask. */
const MIN_QUERY = 2;
const DEBOUNCE_MS = 160;

/**
 * Read the rendered navigation and account-menu links in document order, as the Legacy palette
 * does. The sidebar has already applied the server-authorized Areas (ADR-0012).
 */
export function paletteCommands(sidebar: ParentNode): PaletteCommand[] {
  const links = sidebar.querySelectorAll<HTMLAnchorElement>(
    '.sidenav a[href], .account-menu-panel a[href]',
  );
  const pages: PaletteCommand[] = Array.from(links, (link) => ({
    kind: 'Go to',
    label: link.textContent?.trim() ?? '',
    url: link.getAttribute('href')!,
    angular: link.hasAttribute('data-command-angular'),
  }));
  return pages;
}

/** The chord as this keyboard spells it: ⌘ is a Mac key, so everyone else is shown Ctrl. */
export function commandChord(platform: string): string {
  return /Mac|iPhone|iPad|iPod/.test(platform) ? '⌘K' : 'Ctrl K';
}

interface SecretHit {
  name: string;
  project: string;
  environment: string;
  sensitivity: number;
  sensitivityName: string;
  url: string;
}

function isSecretHit(value: unknown, origin: string): value is SecretHit {
  const hit = value as Partial<SecretHit> | null;
  return (
    typeof hit === 'object' &&
    hit !== null &&
    typeof hit.name === 'string' &&
    typeof hit.project === 'string' &&
    typeof hit.environment === 'string' &&
    typeof hit.sensitivityName === 'string' &&
    Number.isInteger(hit.sensitivity) &&
    hit.sensitivity! >= 1 &&
    hit.sensitivity! <= 4 &&
    typeof hit.url === 'string' &&
    isLocalPath(hit.url, origin)
  );
}

/**
 * A path on this origin, as the browser's own URL parser judges it. A pattern on the raw text is not
 * enough: the parser drops tabs and newlines, so `/\t/host` becomes `//host`, another origin.
 */
function isLocalPath(url: string, origin: string): boolean {
  return url.startsWith('/') && URL.canParse(url, origin) && new URL(url, origin).origin === origin;
}

/**
 * Reads the Legacy UI's `GET /Secrets/Search` answer. That is not a UI API path: an expired session
 * there is redirected to the HTML sign-in page rather than answered with a 401 problem, so anything
 * but a JSON array of results is a failure, never data.
 */
export function secretsFrom(response: HttpResponse<string>, origin: string): PaletteCommand[] {
  if (
    response.redirected ||
    !response.headers.get('Content-Type')?.startsWith('application/json')
  ) {
    throw new Error('Secret search was redirected or did not answer with JSON');
  }
  const body: unknown = JSON.parse(response.body ?? '');
  if (!Array.isArray(body) || !body.every((hit) => isSecretHit(hit, origin))) {
    throw new Error('Secret search answered in an unexpected shape');
  }
  return body.map((hit) => ({
    kind: 'Secret',
    label: hit.name,
    url: hit.url,
    angular: false,
    meta: `${hit.project} · ${hit.environment}`,
    sensitivity: hit.sensitivity,
    sensitivityName: hit.sensitivityName,
  }));
}

/** One search's outcome, tied to the query it answered so it never stands in for another. */
interface Search {
  readonly query: string;
  readonly failed: boolean;
  readonly secrets: readonly PaletteCommand[];
}

/**
 * The ⌘K palette, as the Legacy UI's: jump to a page the sidebar offers, or search secrets through
 * the Legacy UI's existing endpoint, which lists only what the Secrets page would (its decoy and
 * ABAC filtering is the only enumeration path). Results are text bindings only.
 */
@Component({
  selector: 'app-command-palette',
  templateUrl: './command-palette.html',
  host: { '(document:keydown)': 'toggleOnChord($event)' },
})
export class CommandPalette {
  readonly sidebar = input.required<HTMLElement>();

  private readonly document = inject(DOCUMENT);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly location = inject(PAGE_LOCATION);
  private readonly injector = inject(Injector);
  private readonly field = viewChild<ElementRef<HTMLInputElement>>('field');
  private readonly queries = new Subject<string>();
  private opener: Element | null = null;

  protected readonly isOpen = signal(false);
  protected readonly query = signal('');
  private readonly search = signal<Search>({ query: '', failed: false, secrets: [] });
  private readonly pages = signal<readonly PaletteCommand[]>([]);

  private readonly answer = computed(() => {
    const search = this.search();
    return search.query === this.query().trim() ? search : null;
  });
  protected readonly failed = computed(() => this.answer()?.failed ?? false);
  protected readonly results = computed(() => {
    const needle = this.query().trim().toLowerCase();
    const pages = needle
      ? this.pages().filter((page) => page.label.toLowerCase().includes(needle))
      : this.pages();
    return [...(this.answer()?.secrets ?? []), ...pages];
  });
  protected readonly active = linkedSignal({ source: this.results, computation: () => 0 });

  constructor() {
    this.queries
      .pipe(
        // A query too short to search cancels any search in flight at once; a longer one waits for typing to pause.
        debounce((query) => (query.length < MIN_QUERY ? of(0) : timer(DEBOUNCE_MS))),
        // switchMap unsubscribes from the older request, which aborts it.
        switchMap((query) =>
          query.length < MIN_QUERY
            ? of<Search>({ query, failed: false, secrets: [] })
            : this.http
                .get('/Secrets/Search', {
                  params: { q: query },
                  headers: { Accept: 'application/json' },
                  observe: 'response',
                  responseType: 'text',
                })
                .pipe(
                  map((response): Search => ({
                    query,
                    failed: false,
                    secrets: secretsFrom(response, this.document.location.origin),
                  })),
                  catchError(() => of<Search>({ query, failed: true, secrets: [] })),
                ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((search) => this.search.set(search));
  }

  open(): void {
    if (this.isOpen()) return;
    this.opener = this.document.activeElement;
    this.pages.set(paletteCommands(this.sidebar()));
    this.query.set('');
    this.isOpen.set(true);
    afterNextRender(() => this.field()?.nativeElement.focus(), { injector: this.injector });
  }

  protected close(): void {
    if (!this.isOpen()) return;
    this.isOpen.set(false);
    this.query.set('');
    this.queries.next('');
    // Hand focus back where it came from, rather than dumping a keyboard user at the top of the page.
    if (this.opener instanceof HTMLElement && this.document.contains(this.opener))
      this.opener.focus();
    this.opener = null;
  }

  protected toggleOnChord(event: KeyboardEvent): void {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      if (this.isOpen()) this.close();
      else this.open();
    }
  }

  protected typed(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.query.set(value);
    this.queries.next(value.trim());
  }

  protected keydown(event: KeyboardEvent): void {
    const last = this.results().length - 1;
    switch (event.key) {
      case 'Escape':
        this.close();
        break;
      case 'ArrowDown':
        this.highlight(Math.min(this.active() + 1, last));
        break;
      case 'ArrowUp':
        this.highlight(Math.max(this.active() - 1, 0));
        break;
      case 'Enter':
        this.activate(this.active());
        break;
      // The field is the dialog's only focusable element, so holding it is the whole trap.
      case 'Tab':
        break;
      default:
        return;
    }
    event.preventDefault();
  }

  // A press anywhere in the panel would otherwise move focus out of the field and the dialog.
  protected keepFocus(event: MouseEvent): void {
    if (event.target !== this.field()?.nativeElement) event.preventDefault();
  }

  protected closeOutside(event: MouseEvent): void {
    if (event.target === event.currentTarget) this.close();
  }

  protected highlight(index: number): void {
    if (index < 0) return;
    this.active.set(index);
    this.document.getElementById(`palette-option-${index}`)?.scrollIntoView({ block: 'nearest' });
  }

  protected activate(index: number): void {
    const command = this.results()[index];
    if (!command) return;
    this.close();
    if (command.angular) void this.router.navigateByUrl(command.url);
    else this.location.assign(command.url);
  }
}
