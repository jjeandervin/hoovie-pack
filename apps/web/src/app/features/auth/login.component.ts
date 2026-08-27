import { ChangeDetectionStrategy, Component, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'hp-login',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main id="main-content" class="auth-page">
      <section class="auth-story" aria-labelledby="welcome-heading">
        <a class="brand brand--light" routerLink="/login" aria-label="HooviePack">
          <span class="brand-mark" aria-hidden="true"><i></i>H</span>
          <span class="brand-copy"><strong>HooviePack</strong><small>Stay close. Share joy.</small></span>
        </a>

        <div class="auth-story__copy">
          <p class="eyebrow eyebrow--light">Your family’s private corner</p>
          <h1 id="welcome-heading">Every little moment belongs with <em>the pack.</em></h1>
          <p>Share the everyday updates, favorite photos, and happy dog chaos with the people who matter most.</p>
        </div>

        <div class="corgi-scene" aria-hidden="true">
          <span class="scene-sun"></span>
          <span class="scene-hill scene-hill--back"></span>
          <span class="scene-hill"></span>
          <span class="corgi-ear corgi-ear--left"></span>
          <span class="corgi-ear corgi-ear--right"></span>
          <span class="corgi-head"><i class="corgi-eye corgi-eye--left"></i><i class="corgi-eye corgi-eye--right"></i><b></b><small></small></span>
        </div>
      </section>

      <section class="auth-panel" aria-labelledby="signin-heading">
        <div class="auth-card">
          <p class="eyebrow">Welcome home</p>
          <h2 id="signin-heading">Come on in</h2>
          <p class="auth-card__intro">Sign in securely to see what your family has been up to.</p>

          @if (auth.authError()) {
            <div class="alert alert--error" role="alert">
              <span aria-hidden="true">!</span>
              <div><strong>Sign-in is taking a nap</strong><p>{{ auth.authError() }}</p></div>
            </div>
          }

          <button type="button" class="button button--large button--full" (click)="signIn()" [disabled]="!auth.initialized()">
            <span class="button-paw" aria-hidden="true">●</span>
            {{ auth.initialized() ? 'Sign in to HooviePack' : 'Preparing secure sign-in…' }}
          </button>
          <p class="auth-security"><span aria-hidden="true">◇</span> Protected by secure OpenID Connect sign-in</p>

          <div class="auth-divider"><span>Made for your inner circle</span></div>
          <ul class="feature-list">
            <li><span aria-hidden="true">✓</span><div><strong>Private by default</strong><small>Only invited family members can see your pack.</small></div></li>
            <li><span aria-hidden="true">⌁</span><div><strong>All your moments</strong><small>Updates, photos, comments, and plenty of paws.</small></div></li>
            <li><span aria-hidden="true">♥</span><div><strong>Dogs included</strong><small>Give every four-legged family member a profile.</small></div></li>
          </ul>
        </div>
        <p class="auth-footer">A cozy place for the people—and pups—you love.</p>
      </section>
    </main>
  `,
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
