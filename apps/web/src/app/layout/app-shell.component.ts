import { ChangeDetectionStrategy, Component, HostListener, OnInit, computed, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
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
    .mobile-nav-drawer, .mobile-pack-panel { display: none; }
    @media (max-width: 780px) {
      .family-context { display: none; }
      .shell-content { min-height: calc(100vh - 64px); }
      .mobile-header-actions { display: flex; align-items: center; gap: 8px; }
      .mobile-pack-button { display: flex; align-items: center; gap: 5px; min-height: 44px; padding: 0 9px; border: 1px solid var(--line); border-radius: 12px; background: var(--paper); color: var(--ink); font-size: .78rem; font-weight: 750; cursor: pointer; }
      .mobile-pack-panel:popover-open { display: grid; gap: 12px; position: fixed; inset: 70px 12px auto auto; width: min(320px, calc(100% - 24px)); margin: 0; padding: 18px; border: 1px solid var(--line); border-radius: 16px; background: var(--paper); color: var(--ink); box-shadow: 0 12px 36px rgb(57 46 35 / 18%); }
      .mobile-pack-panel label { font-size: .82rem; font-weight: 750; }
      .mobile-pack-panel select { width: 100%; min-width: 0; min-height: 44px; padding: 8px; border: 1px solid var(--line); border-radius: 10px; background: var(--paper-bright); color: var(--ink); font: inherit; }
      .mobile-pack-panel .privacy-chip { justify-self: start; }
      .mobile-nav-drawer { display: block; position: fixed; z-index: 45; bottom: 0; left: 0; right: 0; padding-bottom: env(safe-area-inset-bottom); transition: transform 180ms ease; }
      .mobile-nav-drawer.is-collapsed { transform: translateY(99px); }
      .bottom-nav { position: relative; height: 99px; padding-bottom: 0; }
      .mobile-nav-handle { display: grid; place-items: center; width: 76px; height: 44px; margin: 0 auto; border: 1px solid var(--line); border-bottom: 0; border-radius: 18px 18px 0 0; background: var(--paper); color: var(--muted); touch-action: none; cursor: pointer; }
      .mobile-nav-handle svg { width: 24px; height: 24px; transition: transform 180ms ease; }
      .is-collapsed .mobile-nav-handle svg { transform: rotate(180deg); }
      .bottom-nav a:nth-child(2), .bottom-nav a:nth-child(3) { margin: 0; }
      .bottom-nav .nav-icon { width: 48px; height: 48px; }
      .mobile-fab { right: 16px; bottom: calc(112px + env(safe-area-inset-bottom)); transform: none; padding: 0; transition: bottom 180ms ease; }
      .mobile-fab.nav-collapsed { bottom: calc(16px + env(safe-area-inset-bottom)); }
      .mobile-fab svg { width: 24px; height: 24px; }
    }
    @media (prefers-reduced-motion: reduce) {
      .mobile-nav-drawer, .mobile-nav-handle svg, .mobile-fab { transition: none; }
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
    { label: 'Tools', shortLabel: 'Tools', route: '/tools', icon: '/assets/toolbox.png' },
    { label: 'Your profile', shortLabel: 'Profile', route: '/profile', icon: '/assets/your-profile.png' }
  ];
  readonly mobileNavItems = this.navItems;
  readonly navCollapsed = signal(false);
  private scrollAnchor = 0;
  private gestureStartY: number | null = null;
  private suppressHandleClick = false;
  private readonly currentUrl;
  readonly composing;

  constructor(
    readonly families: ActiveFamilyService,
    readonly user: CurrentUserService,
    readonly auth: AuthService,
    private readonly router: Router
  ) {
    this.currentUrl = toSignal(this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(event => event.urlAfterRedirects)
    ), { initialValue: this.router.url });
    this.composing = computed(() => /^\/posts\/(new|[^/?]+\/edit)(?:[?#]|$)/.test(this.currentUrl()));
  }

  @HostListener('window:scroll')
  onScroll(): void {
    const position = Math.max(0, window.scrollY);
    if (position < this.scrollAnchor) this.scrollAnchor = position;
    if (position > 80 && position - this.scrollAnchor > 32) {
      this.navCollapsed.set(true);
      this.scrollAnchor = position;
    }
  }

  toggleNav(): void {
    if (this.suppressHandleClick) {
      this.suppressHandleClick = false;
      return;
    }
    this.navCollapsed.update(value => !value);
    this.scrollAnchor = window.scrollY;
  }

  startNavGesture(event: PointerEvent): void {
    this.gestureStartY = event.clientY;
    this.suppressHandleClick = false;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
  }

  endNavGesture(event: PointerEvent): void {
    if (this.gestureStartY === null) return;
    const distance = event.clientY - this.gestureStartY;
    this.gestureStartY = null;
    if (Math.abs(distance) < 16) return;
    this.navCollapsed.set(distance > 0);
    this.scrollAnchor = window.scrollY;
    this.suppressHandleClick = true;
  }

  cancelNavGesture(): void {
    this.gestureStartY = null;
    this.suppressHandleClick = false;
  }

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
    (event.target as HTMLElement).closest<HTMLElement>('[popover]')?.hidePopover();
    void this.router.navigateByUrl('/feed');
  }
}
