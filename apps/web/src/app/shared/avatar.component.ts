import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { AuthImageDirective } from './auth-image.directive';

@Component({
  selector: 'hp-avatar',
  standalone: true,
  imports: [AuthImageDirective],
  templateUrl: './avatar.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AvatarComponent {
  readonly src = input<string | null | undefined>(null);
  readonly name = input('');
  readonly size = input(44);
  readonly imageFailed = signal(false);
  readonly resolvedSrc = computed(() => this.src());
  readonly initials = computed(() => {
    const pieces = this.name().trim().split(/\s+/).filter(Boolean);
    return pieces.slice(0, 2).map((piece) => piece[0]).join('').toUpperCase() || 'HP';
  });
}
