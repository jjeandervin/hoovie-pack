import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { UiStateComponent } from '../../shared/ui-state.component';

@Component({
  selector: 'hp-auth-callback',
  standalone: true,
  imports: [RouterLink, UiStateComponent],
  templateUrl: './auth-callback.component.html',
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
