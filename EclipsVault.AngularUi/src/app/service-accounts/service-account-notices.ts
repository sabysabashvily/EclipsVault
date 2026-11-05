import { Injectable } from '@angular/core';

/** Navigation notices carry an account id or fixed text, never key material. */
@Injectable({ providedIn: 'root' })
export class ServiceAccountNotices {
  createdId: string | null = null;
  deleted = false;
}
