import { ChangeDetectionStrategy, Component, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'hp-login',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './login.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LoginComponent implements OnInit {
  constructor(
    readonly auth: AuthService,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    if (this.auth.hasValidToken()) void this.router.navigateByUrl('/feed');
  }

  signIn(): void {
    if (this.auth.authError()) {
      void this.auth.retryInitialization().then(() => {
        if (!this.auth.authError()) this.auth.login('/feed');
      });
      return;
    }
    this.auth.login('/feed');
  }
}
