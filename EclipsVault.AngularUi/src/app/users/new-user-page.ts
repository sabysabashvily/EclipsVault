import { HttpClient, httpResource } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { passwordStrength } from '../profile/password-strength';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import type { components } from '../ui-api/schema.generated';
import { UserFormWrites } from './user-form-writes';

const standardClearance = 1;
const minimumPasswordLength = 12;
const maximumPasswordLength = 256;
const breachPreviewDelayMilliseconds = 400;

@Component({
  selector: 'app-new-user-page',
  imports: [RouterLink, StatusLine, StepUpDialog],
  providers: [UserFormWrites],
  templateUrl: './new-user-page.html',
})
export class NewUserPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly passwordInput = viewChild<ElementRef<HTMLInputElement>>('passwordInput');
  private readonly submitButton = viewChild.required<ElementRef<HTMLButtonElement>>('submitButton');
  private readonly stepUp = viewChild.required(StepUpDialog);
  protected readonly writes = inject(UserFormWrites);
  protected readonly metadata = httpResource<components['schemas']['NewUserResponse']>(
    () => '/api/ui/users/new',
  );
  protected readonly firstName = signal('');
  protected readonly lastName = signal('');
  protected readonly username = signal('');
  protected readonly project = signal('');
  protected readonly clearance = signal(standardClearance);
  protected readonly candidate = signal('');
  protected readonly strength = computed(() => passwordStrength(this.candidate()));
  protected readonly shown = signal(false);
  protected readonly breach = signal<boolean | null>(null);
  private timer: ReturnType<typeof setTimeout> | undefined;
  private preview: Subscription | undefined;

  constructor() {
    this.writes.onClear(() => this.clearPassword());
  }

  protected passwordChanged(value: string): void {
    clearTimeout(this.timer);
    this.preview?.unsubscribe();
    this.candidate.set(value);
    this.breach.set(null);
    if (value.length < minimumPasswordLength || value.length > maximumPasswordLength) return;
    this.timer = setTimeout(() => {
      this.preview = this.http
        .post<components['schemas']['PasswordCheckResponse']>('/api/ui/passwords/check-breached', {
          password: value,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (result) => this.breach.set(result.compromised),
          error: () => undefined,
        });
    }, breachPreviewDelayMilliseconds);
  }

  protected error(field: string): string {
    return this.writes.errors()[field]?.[0] ?? '';
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.breach() || !this.metadata.hasValue()) return;
    const request: components['schemas']['CreateUserUiRequest'] = {
      firstName: this.firstName(),
      lastName: this.lastName(),
      username: this.username(),
      password: this.candidate(),
      clearance: this.clearance(),
      project: this.project(),
    };
    this.writes.send(
      () => this.http.post<components['schemas']['CreatedUserResponse']>('/api/ui/users', request),
      () => {
        this.clearPassword();
        void this.router.navigateByUrl('/Admin/Users');
      },
      (proof) => this.stepUp().open(proof),
    );
  }

  private clearPassword(): void {
    clearTimeout(this.timer);
    this.preview?.unsubscribe();
    const input = this.passwordInput()?.nativeElement;
    if (input) input.value = '';
    this.candidate.set('');
    this.breach.set(null);
    this.shown.set(false);
  }

  protected verified(verified: boolean): void {
    this.writes.verified(verified, this.submitButton().nativeElement);
    if (!verified) this.clearPassword();
  }
}
