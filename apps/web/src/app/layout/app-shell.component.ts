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
  icon: string;
}

@Component({
  selector: 'hp-app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, AvatarComponent, UiStateComponent],
  templateUrl: './app-shell.component.html',
  styles: `
    @media (max-width: 780px) {
      .bottom-nav a:nth-child(2), .bottom-nav a:nth-child(3) { margin: 0; }
      .bottom-nav .nav-icon { width: 48px; height: 48px; }
      .mobile-fab { right: 16px; bottom: calc(112px + env(safe-area-inset-bottom)); transform: none; }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppShellComponent implements OnInit {
  readonly initializing = signal(true);
  readonly shellError = signal('');
  readonly navItems: NavItem[] = [
    { label: 'Family feed', shortLabel: 'Home', route: '/feed', icon: '/assets/family-feed.png' },
    { label: 'The family', shortLabel: 'Family', route: '/family', icon: '/assets/the-family.png' },
    { label: 'Dogs of the family', shortLabel: 'Dogs', route: '/dogs', icon: '/assets/dogs-of-the-family.png' },
    { label: 'Dogipedia', shortLabel: 'Dogipedia', route: '/dogipedia', icon: '/assets/dogipedia.svg' },
    { label: 'Your profile', shortLabel: 'Profile', route: '/profile', icon: '/assets/your-profile.png' }
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
