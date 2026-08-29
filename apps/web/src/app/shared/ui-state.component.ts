import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

@Component({
  selector: 'hp-ui-state',
  standalone: true,
  templateUrl: './ui-state.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UiStateComponent {
  readonly kind = input<'loading' | 'empty' | 'error'>('empty');
  readonly heading = input.required<string>();
  readonly message = input('');
  readonly icon = input('🐾');
  readonly actionLabel = input('');
  readonly compact = input(false);
  readonly action = output<void>();
}
