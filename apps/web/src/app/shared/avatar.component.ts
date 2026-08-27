import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { AuthImageDirective } from './auth-image.directive';

@Component({
  selector: 'hp-avatar',
  standalone: true,
  imports: [AuthImageDirective],
  template: `
    <span
      class="avatar"
      [style.width.px]="size()"
      [style.height.px]="size()"
      [style.font-size.px]="size() * 0.34"
      [attr.aria-label]="name() || 'Profile'"
    >
      @if (resolvedSrc() && !imageFailed()) {
        <img [hpAuthImage]="src()" [alt]="name() ? name() + ' profile photo' : 'Profile photo'" (authImageError)="imageFailed.set(true)" (error)="imageFailed.set(true)">
      } @else {
        <span aria-hidden="true">{{ initials() }}</span>
      }
    </span>
  `,
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
