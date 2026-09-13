import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { catchError, of, startWith, Subject, switchMap } from 'rxjs';
import { UiStateComponent } from '../../shared/ui-state.component';
import { DogipediaApiService } from './dogipedia-api.service';
import { BreedImageComponent } from './breed-image.component';
import { BreedGalleryComponent } from './breed-gallery.component';
import { BreedDetail, BreedTraits } from './dogipedia.models';
import { browseState, measurement, safeExternalUrl } from './dogipedia.helpers';

const TRAITS: [keyof Omit<BreedTraits, 'temperament' | 'exerciseMinutes'>, string][] = [
  ['energy', 'Energy'], ['trainability', 'Trainability'], ['barking', 'Barking'],
  ['grooming', 'Grooming'], ['shedding', 'Shedding'], ['drooling', 'Drooling'],
  ['goodWithChildren', 'Good with children'], ['goodWithDogs', 'Good with dogs'],
  ['goodWithStrangers', 'Good with strangers'], ['apartmentFriendly', 'Apartment friendly']
];

@Component({
  selector: 'hp-breed-detail', standalone: true,
  imports: [RouterLink, UiStateComponent, BreedImageComponent, BreedGalleryComponent],
  templateUrl: './breed-detail.component.html', styleUrl: './dogipedia.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BreedDetailComponent {
  private readonly api = inject(DogipediaApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly title = inject(Title);
  private readonly retry = new Subject<void>();
  readonly backState = browseState(this.route.snapshot.queryParamMap);
  readonly breed = signal<BreedDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<'missing' | 'error' | ''>('');
  readonly range = measurement;
  readonly safeUrl = safeExternalUrl;
  readonly dots = [1, 2, 3, 4, 5];
  readonly traits = computed(() => {
    const breed = this.breed();
    return breed ? TRAITS.flatMap(([key, label]) => {
      const value = breed.traits[key];
      return value === null ? [] : [{ label, value }];
    }) : [];
  });

  constructor() {
    this.route.paramMap.pipe(switchMap(params => this.retry.pipe(startWith(undefined), switchMap(() => {
      this.loading.set(true); this.error.set(''); this.breed.set(null);
      return this.api.get(params.get('id') || '').pipe(catchError(error => {
        this.error.set(error.status === 404 ? 'missing' : 'error');
        return of(null);
      }));
    }))), takeUntilDestroyed(inject(DestroyRef))).subscribe(breed => {
      this.breed.set(breed); this.loading.set(false);
      if (breed) this.title.setTitle(`${breed.name} · Dogipedia · HooviePack`);
      // Focus the stable page region without waiting for the conditional heading to render.
      document.getElementById('breed-detail')?.focus({ preventScroll: true });
      if (breed) window.scrollTo({ top: 0 });
    });
  }

  reload(): void { this.retry.next(); }
}
