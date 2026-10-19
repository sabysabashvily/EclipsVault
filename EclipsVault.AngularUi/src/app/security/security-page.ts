import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import type { components, paths } from '../ui-api/schema.generated';

type Security = paths['/api/ui/security']['get']['responses'][200]['content']['application/json'];
type Check = components['schemas']['SecurityCheckResponse'];

export const grades: Record<Security['grade'], { label: string; class: string }> = {
  Strong: { label: 'Strong', class: 'grade-strong' },
  Good: { label: 'Good', class: 'grade-good' },
  Fair: { label: 'Fair', class: 'grade-fair' },
  AtRisk: { label: 'At risk', class: 'grade-atrisk' },
};

const statuses: Record<Check['status'], { label: string; tone: string; class: string }> = {
  Pass: { label: 'Secured', tone: 'ok', class: 'check-pass' },
  Recommended: { label: 'Recommended', tone: 'warn', class: 'check-warn' },
  ActionNeeded: { label: 'Action needed', tone: 'danger', class: 'check-fail' },
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

/** The Legacy ring's arc and gap, bound only as an SVG presentation attribute. */
export function ringDasharray(score: number): string {
  const circumference = 2 * Math.PI * 52;
  const arc = (circumference * Math.min(100, Math.max(0, score))) / 100;
  return `${arc.toFixed(1)} ${(circumference - arc).toFixed(1)}`;
}

/** The caller's posture, already scored and ordered by Core. All mappings here are presentation. */
@Component({
  selector: 'app-security-page',
  templateUrl: './security-page.html',
})
export class SecurityPage {
  protected readonly security = httpResource<Security>(() => '/api/ui/security');

  protected readonly view = computed(() => {
    if (!this.security.hasValue()) return null;
    const model = this.security.value();
    return {
      ...model,
      grade: grades[model.grade],
      dasharray: ringDasharray(Number(model.score)),
      actionNeededCount: Number(model.actionNeededCount),
      recommendedCount: Number(model.recommendedCount),
      checks: model.checks.map((check) => ({
        ...check,
        standing: statuses[check.status],
        url: fixUrls[check.fix],
        fixLabel: fixLabels[check.kind],
      })),
    };
  });
}
