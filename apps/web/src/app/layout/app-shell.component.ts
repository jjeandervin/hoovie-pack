import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ActiveFamilyService } from '../core/active-family.service';
import { CurrentUserService } from '../core/current-user.service';
import { AuthService } from '../core/auth.service';
import { apiErrorMessage } from '../core/api-error';
import { AvatarComponent } from '../shared/avatar.component';
import { UiStateComponent } from '../shared/ui-state.component';

interface NavItem {
  label: string;
  shortLabel: string;
  route: string;
  symbol: string;
}

@Component({
  selector: 'hp-app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, AvatarComponent, UiStateComponent],
  template: `
    <div class="app-shell">
      <aside class="side-rail" aria-label="Primary navigation">
        <a class="brand brand--rail" routerLink="/feed" aria-label="HooviePack home">
          <span class="brand-mark" aria-hidden="true"><i></i>H</span>
          <span class="brand-copy"><strong>HooviePack</strong><small>Stay close. Share joy.</small></span>
        </a>

        <nav class="rail-nav">
          @for (item of navItems; track item.route) {
            <a [routerLink]="item.route" routerLinkActive="is-active">
              <span class="nav-symbol" aria-hidden="true">{{ item.symbol }}</span>
              <span>{{ item.label }}</span>
            </a>
          }
        </nav>

        <a routerLink="/posts/new" class="button rail-compose"><span aria-hidden="true">＋</span> Share an update</a>

        <div class="rail-profile">
          <hp-avatar [src]="user.profile()?.avatarUrl" [name]="user.profile()?.displayName || auth.displayName()" [size]="42" />
          <div><strong>{{ user.profile()?.displayName || auth.displayName() }}</strong><a routerLink="/profile">View profile</a></div>
          <button type="button" class="icon-button" (click)="auth.logout()" aria-label="Sign out">↗</button>
        </div>
      </aside>

      <div class="shell-main">
        <header class="mobile-header">
          <a class="brand" routerLink="/feed" aria-label="HooviePack home">
            <span class="brand-mark" aria-hidden="true"><i></i>H</span>
            <span class="brand-copy"><strong>HooviePack</strong></span>
          </a>
          <a routerLink="/profile" aria-label="Open your profile">
            <hp-avatar [src]="user.profile()?.avatarUrl" [name]="user.profile()?.displayName || auth.displayName()" [size]="38" />
          </a>
        </header>

        @if (initializing()) {
          <main id="main-content" class="page page--center">
            <hp-ui-state kind="loading" heading="Gathering the pack…" message="Fetching your private family space." />
          </main>
        } @else if (shellError()) {
          <main id="main-content" class="page page--center">
            <hp-ui-state kind="error" heading="We lost the scent" [message]="shellError()" actionLabel="Try again" (action)="initialize()" />
          </main>
        } @else {
          @if (families.families().length > 0) {
            <div class="family-context">
              <label for="active-family">Your pack</label>
              <select id="active-family" [value]="families.activeId() || ''" (change)="changeFamily($event)">
                @for (family of families.families(); track family.id) {
                  <option [value]="family.id">{{ family.name }}</option>
                }
              </select>
              <span class="privacy-chip"><span aria-hidden="true">●</span> Private</span>
            </div>
          }
          <main id="main-content" class="shell-content" tabindex="-1">
            <router-outlet />
          </main>
        }
      </div>

      <a class="mobile-fab" routerLink="/posts/new" aria-label="Create a new post"><span aria-hidden="true">＋</span></a>
      <nav class="bottom-nav" aria-label="Primary navigation">
        @for (item of mobileNavItems; track item.route) {
          <a [routerLink]="item.route" routerLinkActive="is-active">
            <span class="nav-symbol" aria-hidden="true">{{ item.symbol }}</span>
            <span>{{ item.shortLabel }}</span>
          </a>
        }
      </nav>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppShellComponent implements OnInit {
  readonly initializing = signal(true);
  readonly shellError = signal('');
  readonly navItems: NavItem[] = [
    { label: 'Family feed', shortLabel: 'Home', route: '/feed', symbol: '⌂' },
    { label: 'The family', shortLabel: 'Family', route: '/family', symbol: '♧' },
    { label: 'Dogs of the family', shortLabel: 'Dogs', route: '/dogs', symbol: '●' },
    { label: 'Your profile', shortLabel: 'Profile', route: '/profile', symbol: '○' }
  ];
  readonly mobileNavItems = this.navItems;

  constructor(
    readonly families: ActiveFamilyService,
    readonly user: CurrentUserService,
    readonly auth: AuthService,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    void this.initialize();
  }

  async initialize(): Promise<void> {
    this.initializing.set(true);
    this.shellError.set('');
    try {
      const [families] = await Promise.all([this.families.load(true), this.user.load()]);
      if (!families.length) await this.router.navigateByUrl('/onboarding');
    } catch (error) {
      this.shellError.set(apiErrorMessage(error, 'We could not open your family space.'));
    } finally {
      this.initializing.set(false);
    }
  }

  changeFamily(event: Event): void {
    const familyId = (event.target as HTMLSelectElement).value;
    this.families.select(familyId);
    void this.router.navigateByUrl('/feed');
  }
}
