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
  templateUrl: './dogs.component.html',
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
