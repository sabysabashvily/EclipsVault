import { Component, input } from '@angular/core';

@Component({
  selector: 'app-status-line',
  template: `
    <div role="status" aria-atomic="true">
      @if (message() && !error()) {
        <p class="flash flash-success">{{ message() }}</p>
      }
    </div>
    <div role="alert" aria-atomic="true">
      @if (message() && error()) {
        <p class="flash flash-error">{{ message() }}</p>
      }
    </div>
  `,
})
export class StatusLine {
  readonly message = input('');
  readonly error = input(false);
}
