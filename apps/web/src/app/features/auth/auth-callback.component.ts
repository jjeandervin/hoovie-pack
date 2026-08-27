import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { UiStateComponent } from '../../shared/ui-state.component';

@Component({
  selector: 'hp-auth-callback',
  standalone: true,
  imports: [RouterLink, UiStateComponent],
  template: `
    <main id="main-content" class="standalone-page standalone-page--center">
      <a class="brand" routerLink="/login" aria-label="HooviePack">
        <span class="brand-mark" aria-hidden="true"><i></i>H</span>
        <span class="brand-copy"><strong>HooviePack</strong></span>
      </a>
      @if (failed()) {
        <hp-ui-state kind="error" heading="We couldn't finish signing you in" message="Your secure sign-in may have expired. Start once more and we'll get you home." actionLabel="Back to sign in" (action)="backToLogin()" />
      } @else {
        <hp-ui-state kind="loading" heading="Opening the gate…" message="Your pack is just on the other side." />
      }
    </main>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AuthCallbackComponent implements OnInit {
  readonly failed = signal(false);

  constructor(
    private readonly auth: AuthService,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    if (this.auth.hasValidToken()) {
      void this.router.navigateByUrl(this.auth.consumeReturnUrl());
    } else {
      this.failed.set(true);
    }
  }

  backToLogin(): void {
    void this.router.navigateByUrl('/login');
  }
}
