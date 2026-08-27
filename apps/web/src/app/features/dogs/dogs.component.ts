import { ChangeDetectionStrategy, Component, computed, effect, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { DogProfile } from '../../core/models';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { UiStateComponent } from '../../shared/ui-state.component';
import { AuthImageDirective } from '../../shared/auth-image.directive';

@Component({
  selector: 'hp-dogs',
  standalone: true,
  imports: [FormsModule, RouterLink, UiStateComponent, AuthImageDirective],
  template: `
    <div class="page dogs-page">
      <header class="page-heading page-heading--actions">
        <div><p class="eyebrow">Four paws, full hearts</p><h1>Dogs of the family</h1><p>The personalities, favorite things, and very good faces of {{ families.activeFamily()?.name }}.</p></div>
        <a class="button" routerLink="/dogs/new"><span aria-hidden="true">＋</span> Add a pup</a>
      </header>

      <section class="dog-quote" aria-label="A note about family dogs">
        <span class="dog-quote__mark" aria-hidden="true">●</span>
        <blockquote>“Dogs are not our whole life, but they make our lives whole.”</blockquote>
        <div aria-hidden="true"><i></i><i></i><i></i></div>
      </section>

      @if (loading()) {
        <div class="dog-grid dog-grid--skeleton" aria-label="Loading dog profiles" aria-busy="true">@for (item of [1,2,3]; track item) { <div><figure></figure><p></p><span></span></div> }</div>
      } @else if (error()) {
        <hp-ui-state kind="error" heading="The pups are playing hide-and-seek" [message]="error()" actionLabel="Try again" (action)="load()" />
      } @else if (!dogs().length) {
        <hp-ui-state kind="empty" icon="●" heading="No pups in the pack yet" message="Add a dog profile for the four-legged family member who keeps everyone smiling." actionLabel="Add the first pup" (action)="addDog()" />
      } @else {
        <div class="filter-row">
          <label for="dog-search" class="sr-only">Search dogs</label>
          <span aria-hidden="true">⌕</span><input id="dog-search" type="search" [(ngModel)]="query" placeholder="Find a pup…">
          <small>{{ filteredDogs().length }} {{ filteredDogs().length === 1 ? 'pup' : 'pups' }}</small>
        </div>
        @if (filteredDogs().length) {
          <section class="dog-grid" aria-label="Family dog profiles">
            @for (dog of filteredDogs(); track dog.id; let index = $index) {
              <article class="dog-card" [class.dog-card--featured]="index === 0">
                <a [routerLink]="['/dogs', dog.id]" class="dog-card__photo">
                  @if (dog.photoUrl) { <img [hpAuthImage]="dog.photoUrl" [alt]="dog.name" loading="lazy"> } @else { <span class="dog-placeholder" aria-hidden="true"><i></i>●</span> }
                  @if (dog.birthday) { <small>{{ ageLabel(dog.birthday) }}</small> } @else if (dog.approximateAge) { <small>About {{ dog.approximateAge }}</small> }
                </a>
                <div class="dog-card__body">
                  <p class="eyebrow">{{ dog.breed || 'Very good dog' }}</p>
                  <h2><a [routerLink]="['/dogs', dog.id]">{{ dog.name }}</a></h2>
                  <p>{{ dog.bio || 'An important member of the family with excellent taste in treats.' }}</p>
                  @if (dog.favoriteThing) { <div class="favorite-chip"><span aria-hidden="true">♥</span><span><small>Favorite thing</small><strong>{{ dog.favoriteThing }}</strong></span></div> }
                  <a [routerLink]="['/dogs', dog.id]" class="text-link">Meet {{ dog.name }} <span aria-hidden="true">→</span></a>
                </div>
              </article>
            }
          </section>
        } @else {
          <hp-ui-state kind="empty" icon="⌕" heading="No matching pups" message="Try a different name or breed." [compact]="true" />
        }
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DogsComponent {
  readonly dogs = signal<DogProfile[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly querySignal = signal('');
  readonly filteredDogs = computed(() => {
    const query = this.querySignal().trim().toLowerCase();
    if (!query) return this.dogs();
    return this.dogs().filter((dog) => `${dog.name} ${dog.breed || ''}`.toLowerCase().includes(query));
  });
  private loadedFamilyId: string | null = null;

  get query(): string { return this.querySignal(); }
  set query(value: string) { this.querySignal.set(value); }

  constructor(
    readonly families: ActiveFamilyService,
    private readonly api: ApiService,
    private readonly config: RuntimeConfigService,
    private readonly router: Router
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
    this.api.listDogs(familyId).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (dogs) => this.dogs.set(dogs),
      error: (error) => this.error.set(apiErrorMessage(error, 'We could not load the family dogs.'))
    });
  }

  addDog(): void {
    void this.router.navigateByUrl('/dogs/new');
  }

  mediaUrl(path: string): string {
    return this.config.mediaUrl(path) ?? '';
  }

  ageLabel(birthday: string): string {
    const birth = new Date(birthday);
    const months = Math.max(0, Math.floor((Date.now() - birth.getTime()) / 2_629_746_000));
    if (months < 12) return `${months} mo`;
    const years = Math.floor(months / 12);
    return `${years} ${years === 1 ? 'yr' : 'yrs'}`;
  }
}
