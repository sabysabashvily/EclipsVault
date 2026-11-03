import { Component, input, model } from '@angular/core';

@Component({
  selector: 'app-table-filter',
  template: `
    <input
      #field
      type="search"
      class="table-search"
      [attr.aria-label]="label()"
      [placeholder]="placeholder()"
      [value]="query()"
      (input)="query.set(field.value)"
    />
  `,
})
export class TableFilter {
  readonly label = input.required<string>();
  readonly placeholder = input('');
  readonly query = model('');
}
