import { ChangeDetectionStrategy, Component, effect, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, finalize } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { DogProfile, FamilyMember } from '../../core/models';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { AvatarComponent } from '../../shared/avatar.component';
import { AuthImageDirective } from '../../shared/auth-image.directive';
import { UiStateComponent } from '../../shared/ui-state.component';

@Component({
  selector: 'hp-family',
  standalone: true,
  imports: [RouterLink, AvatarComponent, AuthImageDirective, UiStateComponent],
  template: `
    <div class="page family-page">
      <header class="page-heading page-heading--actions">
        <div><p class="eyebrow">The family</p><h1>{{ families.activeFamily()?.name }}</h1><p>{{ families.activeFamily()?.description || 'Your private home for shared family moments.' }}</p></div>
        @if (families.canManage()) { <a class="button button--secondary" routerLink="/family/settings">Manage family</a> }
      </header>

      <section class="family-banner">
        <div>
          <span aria-hidden="true">◇</span>
          <p><strong>Private family space</strong><small>Only invited members of {{ families.activeFamily()?.name }} can see what’s shared here.</small></p>
        </div>
        <span class="role-badge">{{ families.activeFamily()?.role }}</span>
      </section>

      @if (loading()) {
        <hp-ui-state kind="loading" heading="Gathering everyone…" [compact]="true" />
      } @else if (error()) {
        <hp-ui-state kind="error" heading="We couldn’t load the family" [message]="error()" actionLabel="Try again" (action)="load()" />
      } @else {
        <section class="section-block" aria-labelledby="members-heading">
          <div class="section-heading">
            <div><p class="eyebrow">People</p><h2 id="members-heading">Pack members <span>{{ members().length }}</span></h2></div>
            @if (families.canManage()) { <a routerLink="/family/settings" [queryParams]="{ invite: 1 }">＋ Invite someone</a> }
          </div>
          @if (members().length) {
            <div class="member-grid">
              @for (member of members(); track member.id) {
                <a class="member-card" [routerLink]="['/members', member.id]">
                  <hp-avatar [src]="member.avatarUrl" [name]="member.displayName" [size]="62" />
                  <div><strong>{{ member.displayName }}</strong><span class="role-chip" [class.role-chip--owner]="member.role === 'Owner'">{{ member.role }}</span><p>{{ member.bio || 'A beloved member of the pack.' }}</p></div>
                  <span aria-hidden="true">›</span>
                </a>
              }
            </div>
          } @else {
            <hp-ui-state kind="empty" heading="It’s just you for now" message="Invite family members to start sharing together." [compact]="true" />
          }
        </section>

        <section class="section-block" aria-labelledby="family-dogs-heading">
          <div class="section-heading">
            <div><p class="eyebrow">Four-legged family</p><h2 id="family-dogs-heading">Dogs in the pack <span>{{ dogs().length }}</span></h2></div>
            <a routerLink="/dogs">See all dogs</a>
          </div>
          @if (dogs().length) {
            <div class="dog-mini-row">
              @for (dog of dogs().slice(0, 4); track dog.id) {
                <a [routerLink]="['/dogs', dog.id]">
                  @if (dog.photoUrl) { <img [hpAuthImage]="dog.photoUrl" [alt]="dog.name"> } @else { <span aria-hidden="true">●</span> }
                  <strong>{{ dog.name }}</strong><small>{{ dog.breed || 'Very good dog' }}</small>
                </a>
              }
              <a class="dog-mini-add" routerLink="/dogs/new"><span aria-hidden="true">＋</span><strong>Add a pup</strong><small>Every dog belongs</small></a>
            </div>
          } @else {
            <a class="inline-empty-card" routerLink="/dogs/new"><span aria-hidden="true">●</span><div><strong>Who are the dogs of the family?</strong><p>Add the first pup and let their personality shine.</p></div><b>＋ Add a pup</b></a>
          }
        </section>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FamilyComponent {
  readonly members = signal<FamilyMember[]>([]);
  readonly dogs = signal<DogProfile[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  private loadedFamilyId: string | null = null;

  constructor(
    readonly families: ActiveFamilyService,
    private readonly api: ApiService,
    private readonly config: RuntimeConfigService
  ) {
    effect(() => {
      const familyId = this.families.activeId();
      if (familyId && familyId !== this.loadedFamilyId) {
        this.loadedFamilyId = familyId;
        this.load();
      }
    });
  }

  load(): void {
    const familyId = this.families.activeId();
    if (!familyId) return;
    this.loading.set(true);
    this.error.set('');
    forkJoin({ members: this.api.listMembers(familyId), dogs: this.api.listDogs(familyId) }).pipe(
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: ({ members, dogs }) => {
        this.members.set(members);
        this.dogs.set(dogs);
      },
      error: (error) => this.error.set(apiErrorMessage(error, 'The family roster is unavailable right now.'))
    });
  }

  mediaUrl(path: string): string {
    return this.config.mediaUrl(path) ?? '';
  }
}
