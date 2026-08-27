import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

@Component({
  selector: 'hp-ui-state',
  standalone: true,
  template: `
    <section class="ui-state" [class.ui-state--compact]="compact()" [attr.aria-busy]="kind() === 'loading'">
      @if (kind() === 'loading') {
        <div class="state-mark state-mark--loading" aria-hidden="true"><span></span><span></span><span></span></div>
      } @else {
        <div class="state-mark" [class.state-mark--error]="kind() === 'error'" aria-hidden="true">
          {{ kind() === 'error' ? '!' : icon() }}
        </div>
      }
      <h2>{{ heading() }}</h2>
      @if (message()) { <p>{{ message() }}</p> }
      @if (actionLabel()) {
        <button type="button" class="button" [class.button--secondary]="kind() !== 'error'" (click)="action.emit()">
          {{ actionLabel() }}
        </button>
      }
    </section>
  `,
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
