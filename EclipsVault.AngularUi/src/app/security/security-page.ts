import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { components, paths } from '../ui-api/schema.generated';

type Security = paths['/api/ui/security']['get']['responses'][200]['content']['application/json'];
type Check = components['schemas']['SecurityCheckResponse'];

export const grades: Record<Security['grade'], { label: string; tone: string }> = {
  Strong: { label: 'Strong', tone: 'ok' },
  Good: { label: 'Good', tone: 'ok' },
  Fair: { label: 'Fair', tone: 'warn' },
  AtRisk: { label: 'At risk', tone: 'danger' },
};

const statuses: Record<Exclude<Check['status'], 'Pass'>, { label: string; tone: string }> = {
  Recommended: { label: 'Recommended', tone: 'warn' },
  ActionNeeded: { label: 'Action needed', tone: 'danger' },
};

export const fixUrls: Record<Check['fix'], string | null> = {
  None: null,
  Profile: '/Profile',
  BackupCodes: '/Profile',
  SignedInDevices: '/Sessions',
};

export const fixLabels: Record<Check['kind'], string> = {
  TwoStepSignIn: 'Set up two-step',
  BackupCodes: 'Generate codes',
  Passkey: 'Add a passkey',
  SignedInDevices: 'Review devices',
};

/** The title's verdict: the most urgent standing among the checks. */
export function verdict(actionNeeded: number, recommended: number) {
  if (actionNeeded > 0) return { label: 'Action needed', tone: 'danger' };
  if (recommended > 0) return { label: 'Recommended', tone: 'warn' };
  return { label: 'All good', tone: 'ok' };
}

/** The line under the title: how many checks need you and the next step, or that all pass. */
export function context(open: number, passCount: number, nextStep: string | null): string {
  if (open === 0)
    return `All ${passCount} checks pass. Everything here is about your account only.`;
  const count = open === 1 ? '1 check needs you' : `${open} checks need you`;
  return nextStep === null ? `${count}.` : `${count} · next: ${nextStep}`;
}

/** The caller's posture, already scored and ordered by Core. All mappings here are presentation. */
@Component({
  selector: 'app-security-page',
  imports: [RouterLink],
  templateUrl: './security-page.html',
})
export class SecurityPage {
  protected readonly security = httpResource<Security>(() => '/api/ui/security');

  protected readonly view = computed(() => {
    if (!this.security.hasValue()) return null;
    const model = this.security.value();
    const score = Number(model.score);
    const actionNeeded = Number(model.actionNeededCount);
    const recommended = Number(model.recommendedCount);
    const open = actionNeeded + recommended;
    return {
      score,
      // The bar's fill, bound as an SVG width so no inline style is needed (CSP-clean).
      fill: Math.min(100, Math.max(0, score)),
      grade: grades[model.grade],
      open,
      verdict: verdict(actionNeeded, recommended),
      context: context(open, Number(model.passCount), model.nextStep),
      failing: model.checks.flatMap((check) =>
        check.status === 'Pass'
          ? []
          : [
              {
                ...check,
                standing: statuses[check.status],
                url: fixUrls[check.fix],
                fixLabel: fixLabels[check.kind],
              },
            ],
      ),
      passing: model.checks.filter((check) => check.status === 'Pass'),
    };
  });
}
