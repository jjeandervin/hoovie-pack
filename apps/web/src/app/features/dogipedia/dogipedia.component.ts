import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, map, of, startWith, merge, Subject, switchMap, tap } from 'rxjs';
import { UiStateComponent } from '../../shared/ui-state.component';
import { DogipediaApiService } from './dogipedia-api.service';
import { BreedImageComponent } from './breed-image.component';
import { BreedPage } from './dogipedia.models';
import { DogipediaAdvancedFiltersComponent } from './dogipedia-advanced-filters.component';
import { AdvancedFilters, DogipediaBreedSearchState, FilterOptions, FacetKey, clearFilters, filterChips, searchParams, searchState } from './dogipedia-search';
import { measurement } from './dogipedia.helpers';

@Component({
  selector: 'hp-dogipedia', standalone: true,
  imports: [ReactiveFormsModule, RouterLink, UiStateComponent, BreedImageComponent, DogipediaAdvancedFiltersComponent],
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
  readonly state = signal<DogipediaBreedSearchState>(searchState(this.route.snapshot.queryParamMap));
  readonly expanded = signal(false);
  readonly options = signal<FilterOptions | null>(null);
  readonly optionsError = signal(false);
  private readonly changes = new Subject<void>();
  readonly chips = computed(() => filterChips(this.state(), this.options()));
  readonly params = computed(() => searchParams(this.state()));
  readonly result = signal<BreedPage | null>(null);
  readonly loading = signal(true);
  readonly error = signal<'unavailable' | 'error' | ''>('');
  readonly range = measurement;

  constructor() {
    this.route.queryParamMap.pipe(
      map(searchState), distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
      tap(state => {
        this.state.set(state); this.query.setValue(state.search, { emitEvent: false });
        if (state.breedGroupIds?.length && !this.options()) this.loadOptions();
      }),
      switchMap(state => this.retry.pipe(startWith(undefined), switchMap(() => {
        this.loading.set(true); this.error.set('');
        return this.api.search(state).pipe(
          catchError(error => {
            this.error.set(error.status === 503 && error.error?.code === 'dogipedia_catalog_unavailable' ? 'unavailable' : 'error');
            return of(null);
          })
        );
      }))), takeUntilDestroyed(this.destroyRef)
    ).subscribe(result => { this.result.set(result); this.loading.set(false); });

    // Navigation cancels pending edits, including when the user presses Back/Forward.
    this.route.queryParamMap.pipe(
      switchMap(() => merge(this.query.valueChanges, this.changes).pipe(debounceTime(300))),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(() => this.navigate({ ...this.state(), search: this.query.value.trim(), page: 1 }));
  }

  toggleFilters(): void {
    this.expanded.update(value => !value);
    if (this.expanded() && !this.options()) this.loadOptions();
  }
  loadOptions(): void {
    this.optionsError.set(false);
    this.api.filterOptions().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: options => this.options.set(options), error: () => this.optionsError.set(true)
    });
  }
  changeFilters(filters: AdvancedFilters): void {
    this.state.set({ ...filters, search: this.query.value.trim(), page: 1, pageSize: this.state().pageSize, sort: 'name' });
    this.changes.next();
  }
  removeChip(chip: ReturnType<typeof filterChips>[number]): void {
    const state = { ...this.state() };
    for (const key of chip.keys) {
      if (chip.value !== undefined) state[key as FacetKey] = state[key as FacetKey]?.filter(value => value !== chip.value);
      else delete state[key];
    }
    this.changeFilters(state);
  }
  clearAll(): void { this.changeFilters(clearFilters(this.state())); }
  private navigate(state: DogipediaBreedSearchState, scroll: 'manual' | 'after-transition' = 'manual'): void {
    // Search/filter edits update the URL in place without moving the active control.
    void this.router.navigate([], { relativeTo: this.route, queryParams: searchParams(state), scroll });
  }

  reload(): void { this.retry.next(); }
  goToPage(page: number): void {
    this.navigate({ ...this.state(), search: this.query.value.trim(), page }, 'after-transition');
    document.getElementById('breed-results')?.focus({ preventScroll: true });
  }
}
