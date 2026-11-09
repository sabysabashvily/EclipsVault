import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component } from '@angular/core';
import type { components } from '../ui-api/schema.generated';

@Component({
  selector: 'app-license-page',
  imports: [DatePipe],
  templateUrl: './license-page.html',
})
export class LicensePage {
  protected readonly license = httpResource<components['schemas']['LicenseResponse']>(
    () => '/api/ui/license',
  );
  protected readonly tones: Record<components['schemas']['LicenseResponse']['status'], string> = {
    Valid: 'ok',
    Expired: 'warn',
    Missing: 'muted',
    Malformed: 'danger',
    InvalidSignature: 'danger',
  };
}
