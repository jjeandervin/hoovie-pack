import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { DogProfile } from '../../core/models';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { ToastService } from '../../core/toast.service';
import { UiStateComponent } from '../../shared/ui-state.component';
import { AuthImageDirective } from '../../shared/auth-image.directive';

@Component({
  selector: 'hp-dog-detail',
  standalone: true,
  imports: [RouterLink, UiStateComponent, AuthImageDirective],
  templateUrl: './dog-detail.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DogDetailComponent implements OnInit {
  readonly dog = signal<DogProfile | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');

  constructor(
    readonly families: ActiveFamilyService,
    private readonly api: ApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly config: RuntimeConfigService,
    private readonly toasts: ToastService
  ) {}

  ngOnInit(): void {
    const dogId = this.route.snapshot.paramMap.get('dogId');
    const familyId = this.families.activeId();
    if (!dogId || !familyId) {
      this.loading.set(false);
      this.error.set('No dog profile was selected.');
      return;
    }
    this.api.getDog(familyId, dogId).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (dog) => this.dog.set(dog),
      error: (error) => this.error.set(apiErrorMessage(error, 'This dog profile may no longer be available.'))
    });
  }

  deleteDog(): void {
    const dog = this.dog();
    const familyId = this.families.activeId();
    if (!dog || !familyId || !window.confirm(`Delete ${dog.name}’s profile? This cannot be undone.`)) return;
    this.api.deleteDog(familyId, dog.id).subscribe({
      next: () => { this.toasts.success(`${dog.name}’s profile was removed.`); this.goToDogs(); },
      error: (error) => this.toasts.error(apiErrorMessage(error, 'This dog profile could not be deleted.'))
    });
  }

  mediaUrl(path: string): string { return this.config.mediaUrl(path) ?? ''; }
  goToDogs(): void { void this.router.navigateByUrl('/dogs'); }
  birthday(value: string): string { return new Date(value).toLocaleDateString(undefined, { month: 'long', day: 'numeric', year: 'numeric' }); }
  fullAge(value: string): string {
    const birthday = new Date(value);
    const months = Math.max(0, Math.floor((Date.now() - birthday.getTime()) / 2_629_746_000));
    if (months < 12) return `${months} ${months === 1 ? 'month' : 'months'} old`;
    const years = Math.floor(months / 12);
    return `${years} ${years === 1 ? 'year' : 'years'} old`;
  }
}
