import {
  afterNextRender,
  DestroyRef,
  DOCUMENT,
  Injectable,
  Injector,
  inject,
  signal,
} from '@angular/core';
import { Observable, Subject, finalize, takeUntil } from 'rxjs';
import { StepUpProof, UiApiError } from '../ui-api/ui-api-errors';

/** Retains a blocked grant only in page memory and retries it once after fresh proof. */
@Injectable()
export class UserFormWrites {
  readonly busy = signal(false);
  readonly awaitingProof = signal(false);
  readonly blocked = signal(false);
  readonly status = signal({ message: '', error: false });
  readonly errors = signal<Readonly<Record<string, string[]>>>({});
  readonly ended = new Subject<void>();
  private readonly injector = inject(Injector);
  private pending: (() => void) | null = null;
  private readonly clearers = new Set<() => void>();

  constructor() {
    const document = inject(DOCUMENT);
    const lost = () => {
      this.blocked.set(true);
      this.clear();
      this.status.set({
        message: 'Reload the page or sign in before making another change.',
        error: true,
      });
    };
    document.defaultView?.addEventListener('pagehide', lost);
    document.addEventListener('vault-session-ended', lost);
    inject(DestroyRef).onDestroy(() => {
      document.defaultView?.removeEventListener('pagehide', lost);
      document.removeEventListener('vault-session-ended', lost);
      this.clear();
      this.ended.complete();
    });
  }

  onClear(clear: () => void): void {
    this.clearers.add(clear);
  }

  clear(): void {
    this.pending = null;
    this.awaitingProof.set(false);
    this.ended.next();
    this.clearers.forEach((clear) => clear());
  }

  verified(verified: boolean, opener: HTMLButtonElement): void {
    const retry = this.pending;
    this.pending = null;
    this.awaitingProof.set(false);
    if (verified) retry?.();
    else afterNextRender(() => opener.focus(), { injector: this.injector });
  }

  send<T>(
    request: () => Observable<T>,
    saved: (value: T) => void,
    prompt: (proof: StepUpProof) => void,
    allowStepUp = true,
  ): void {
    if (this.busy() || this.awaitingProof() || this.blocked()) return;
    this.busy.set(true);
    this.status.set({ message: '', error: false });
    this.errors.set({});
    request()
      .pipe(
        takeUntil(this.ended),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: saved,
        error: (error: unknown) => {
          if (
            error instanceof UiApiError &&
            error.code === 'step_up_required' &&
            error.proof &&
            allowStepUp
          ) {
            this.pending = () => this.send(request, saved, prompt, false);
            this.awaitingProof.set(true);
            prompt(error.proof);
            return;
          }
          const unknownOutcome =
            !(error instanceof UiApiError) || error.status === 0 || error.code === 'unknown';
          if (unknownOutcome) {
            this.blocked.set(true);
            this.clear();
          }
          this.errors.set(error instanceof UiApiError ? error.errors : {});
          this.status.set({
            message:
              !unknownOutcome && error instanceof UiApiError
                ? error.title
                : 'The response was lost. Check Users before trying again.',
            error: true,
          });
        },
      });
  }
}
