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
  templateUrl: './family.component.html',
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
