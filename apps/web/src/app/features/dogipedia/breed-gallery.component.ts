import { ChangeDetectionStrategy, Component, ElementRef, computed, input, signal, viewChild } from '@angular/core';
import { BreedGalleryImage } from './dogipedia.models';
import { BreedImageComponent } from './breed-image.component';
import { safeExternalUrl } from './dogipedia.helpers';

@Component({
  selector: 'hp-breed-gallery', standalone: true,
  imports: [BreedImageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-label="Breed photos">
      <hp-breed-image [image]="selected()" [name]="name()" [detail]="true" />
      @if (images().length > 1) {
        <p class="gallery-position" role="status">Photo {{ selectedIndex() + 1 }} of {{ images().length }}</p>
        <div class="gallery-carousel">
          <button class="gallery-arrow" type="button" aria-label="Previous photo" [disabled]="selectedIndex() === 0" (click)="move(-1)">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m14 6-6 6 6 6" /></svg>
          </button>
        <div #thumbnailStrip class="gallery-thumbnails" role="group" aria-label="Choose a breed photo">
          @for (image of images(); track image.id; let index = $index) {
            <button class="gallery-thumbnail" type="button" [attr.aria-label]="'View image ' + (index + 1) + ' of ' + images().length + ' for ' + name()"
              [attr.aria-pressed]="image.id === selected()?.id" [title]="credit(image)"
              (click)="select(image.id)" (keydown)="navigateThumbnails($event, index)">
              @if (thumbnailUrl(image); as url) {
                @if (!failedThumbnails().has(image.id)) {
                  <img [src]="url" alt="" loading="lazy" (error)="thumbnailFailed(image.id)">
                } @else { <img src="/assets/dogipedia.svg" alt="Photo unavailable" loading="lazy"> }
              } @else { <img src="/assets/dogipedia.svg" alt="Photo unavailable" loading="lazy"> }
              @if (image.id === selected()?.id) { <span aria-hidden="true">✓</span> }
            </button>
          }
        </div>
          <button class="gallery-arrow" type="button" aria-label="Next photo" [disabled]="selectedIndex() === images().length - 1" (click)="move(1)">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m10 6 6 6-6 6" /></svg>
          </button>
        </div>
      }
    </section>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .gallery-position { margin: 0; padding: 10px 14px 0; color: var(--muted); font-size: .75rem; }
    .gallery-carousel { display: grid; grid-template-columns: 44px minmax(0, 1fr) 44px; align-items: center; gap: 8px; padding: 8px 12px 14px; }
    .gallery-thumbnails { display: flex; gap: 10px; overflow-x: auto; padding: 5px; overscroll-behavior-x: contain; scrollbar-width: none; }
    .gallery-thumbnails::-webkit-scrollbar { display: none; }
    .gallery-arrow { display: grid; place-items: center; width: 44px; height: 44px; padding: 0; border: 1px solid var(--line); border-radius: 50%; background: var(--paper); color: var(--sage-800); box-shadow: var(--shadow-sm); cursor: pointer; transition: background .15s, box-shadow .15s; }
    .gallery-arrow:hover:not(:disabled) { background: var(--sage-100); box-shadow: var(--shadow-md); }
    .gallery-arrow:disabled { opacity: .35; box-shadow: none; cursor: default; }
    .gallery-arrow svg { width: 22px; height: 22px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
    .gallery-thumbnail { position: relative; flex: 0 0 88px; width: 88px; height: 78px; border: 2px solid var(--line); border-radius: 10px; padding: 3px; background: var(--sage-100); cursor: pointer; }
    .gallery-thumbnail[aria-pressed="true"] { border-color: var(--sage-800); }
    .gallery-thumbnail img { width: 100%; height: 100%; object-fit: contain; border-radius: 5px; }
    .gallery-thumbnail span { position: absolute; right: 3px; bottom: 3px; min-width: 18px; border-radius: 5px; padding: 1px 4px; background: var(--paper); color: var(--sage-900); font-size: .65rem; font-weight: 750; }
    button:focus-visible { outline-offset: 2px; }
  `
})
export class BreedGalleryComponent {
  readonly images = input.required<BreedGalleryImage[]>();
  readonly name = input.required<string>();
  private readonly selectedId = signal<string | null>(null);
  private readonly thumbnailStrip = viewChild<ElementRef<HTMLElement>>('thumbnailStrip');
  readonly failedThumbnails = signal<ReadonlySet<string>>(new Set());
  readonly selectedIndex = computed(() => Math.max(0, this.images().findIndex(image => image.id === this.selectedId())));
  readonly selected = computed<BreedGalleryImage | null>(() => this.images()[this.selectedIndex()] ?? null);

  select(id: string): void {
    this.selectedId.set(id);
    const strip = this.thumbnailStrip()?.nativeElement;
    const button = strip?.querySelectorAll('button')[this.selectedIndex()];
    if (!strip || !button) return;
    const frame = strip.getBoundingClientRect();
    const thumbnail = button.getBoundingClientRect();
    const left = thumbnail.left < frame.left + 5 ? thumbnail.left - frame.left - 5
      : thumbnail.right > frame.right - 5 ? thumbnail.right - frame.right + 5 : 0;
    // Move only the thumbnail strip; selecting a photo should never move the page.
    strip.scrollBy({ left, behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
  }
  move(direction: number): void {
    const next = this.selectedIndex() + direction;
    if (next >= 0 && next < this.images().length) this.select(this.images()[next].id);
  }
  thumbnailUrl(image: BreedGalleryImage): string | null {
    return safeExternalUrl(image.thumbUrl) || safeExternalUrl(image.mediumUrl) || safeExternalUrl(image.largeUrl);
  }
  credit(image: BreedGalleryImage): string {
    return [image.author, image.license, image.source].filter(Boolean).join(' · ');
  }
  thumbnailFailed(id: string): void {
    this.failedThumbnails.update(failed => new Set([...failed, id]));
  }
  navigateThumbnails(event: KeyboardEvent, index: number): void {
    let next: number;
    switch (event.key) {
      case 'ArrowLeft': next = Math.max(0, index - 1); break;
      case 'ArrowRight': next = Math.min(this.images().length - 1, index + 1); break;
      case 'Home': next = 0; break;
      case 'End': next = this.images().length - 1; break;
      default: return;
    }
    event.preventDefault();
    this.select(this.images()[next].id);
    const button = (event.currentTarget as HTMLElement).parentElement?.querySelectorAll('button')[next];
    button?.focus({ preventScroll: true });
  }
}
