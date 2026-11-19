import { ScrollRegion } from '../shared/scroll-region';
import { httpResource } from '@angular/common/http';
import { Component } from '@angular/core';
import type { paths } from '../ui-api/schema.generated';

type Access = paths['/api/ui/access']['get']['responses'][200]['content']['application/json'];

/** A live, self-scoped explanation. The server makes every access decision. */
@Component({
  selector: 'app-access-page',
  imports: [ScrollRegion],
  templateUrl: './access-page.html',
  // Keep the Legacy page's spaces between inline badges, network address and window hours.
  preserveWhitespaces: true,
})
export class AccessPage {
  protected readonly access = httpResource<Access>(() => '/api/ui/access');

  protected windowRange(access: Access): string {
    return `${String(access.windowStartHour).padStart(2, '0')}:00–${String(access.windowEndHour).padStart(2, '0')}:00 ${access.windowZone}`;
  }
}
