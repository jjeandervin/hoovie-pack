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
  templateUrl: './app-shell.component.html',
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
