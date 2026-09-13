import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, map, of, startWith, Subject, switchMap, tap } from 'rxjs';
import { UiStateComponent } from '../../shared/ui-state.component';
import { DogipediaApiService } from './dogipedia-api.service';
import { BreedImageComponent } from './breed-image.component';
import { BreedPage } from './dogipedia.models';
import { browseState, measurement } from './dogipedia.helpers';

@Component({
  selector: 'hp-dogipedia', standalone: true,
  imports: [ReactiveFormsModule, RouterLink, UiStateComponent, BreedImageComponent],
  templateUrl: './dogipedia.component.html', styleUrl: './dogipedia.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DogipediaComponent {
  private readonly api = inject(DogipediaApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly retry = new Subject<void>();
  readonly query = new FormControl('', { nonNullable: true });
  readonly state = signal({ search: '', page: 1 });
  readonly result = signal<BreedPage | null>(null);
  readonly loading = signal(true);
  readonly error = signal<'unavailable' | 'error' | ''>('');
  readonly range = measurement;

  constructor() {
    this.route.queryParamMap.pipe(
      map(browseState), distinctUntilChanged((a, b) => a.search === b.search && a.page === b.page),
      tap(state => { this.state.set(state); this.query.setValue(state.search, { emitEvent: false }); }),
      switchMap(state => this.retry.pipe(startWith(undefined), switchMap(() => {
        this.loading.set(true); this.error.set('');
        return this.api.list(state.search, state.page).pipe(
          catchError(error => {
            this.error.set(error.status === 503 && error.error?.code === 'dogipedia_catalog_unavailable' ? 'unavailable' : 'error');
            return of(null);
          })
        );
      }))), takeUntilDestroyed(this.destroyRef)
    ).subscribe(result => { this.result.set(result); this.loading.set(false); });

    // Replacing this subscription on navigation also cancels a pending debounce on Back/Forward.
    this.route.queryParamMap.pipe(
      switchMap(() => this.query.valueChanges.pipe(debounceTime(300), map(value => value.trim()), distinctUntilChanged())),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(search => {
      if (search !== this.state().search) void this.router.navigate([], {
        relativeTo: this.route, queryParams: { search: search || null, page: 1 }
      });
    });
  }

  reload(): void { this.retry.next(); }
  goToPage(page: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { search: this.state().search || null, page } });
    document.getElementById('breed-results')?.focus({ preventScroll: true });
  }
}
