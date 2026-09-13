import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { BreedImage } from './dogipedia.models';
import { safeExternalUrl } from './dogipedia.helpers';

@Component({
  selector: 'hp-breed-image',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <figure>
      @if (url(); as src) {
        @if (failedUrl() !== src) {
          <img class="breed-photo" [src]="src" [alt]="name()" [attr.loading]="detail() ? 'eager' : 'lazy'" [attr.fetchpriority]="detail() ? 'high' : 'auto'" (error)="failedUrl.set(src)">
          <figcaption>
            Photo: {{ image()?.author || 'Author not supplied' }}
            @if (image()?.license; as license) {
              · @if (safeUrl(image()?.licenseUrl); as href) { <a [href]="href" target="_blank" rel="noopener noreferrer">{{ license }}</a> } @else { {{ license }} }
            }
            @if (image()?.source; as source) {
              · @if (safeUrl(image()?.sourceUrl); as href) { <a [href]="href" target="_blank" rel="noopener noreferrer">{{ source === 'wikimedia_commons' ? 'Wikimedia Commons' : source }}</a> } @else { {{ source }} }
            }
          </figcaption>
        } @else { <ng-container [ngTemplateOutlet]="placeholder" /> }
      } @else { <ng-container [ngTemplateOutlet]="placeholder" /> }
    </figure>
    <ng-template #placeholder>
      <div class="breed-placeholder" role="img" [attr.aria-label]="'No photo available for ' + name()">
        <img src="/assets/dogipedia.svg" alt="" aria-hidden="true" width="96" height="96">
        <span>Every breed has a story</span><small>Photo coming another day</small>
      </div>
    </ng-template>
  `,
  imports: [NgTemplateOutlet],
  styles: `
    :host { display: block; min-width: 0; }
    figure { margin: 0; }
    .breed-photo, .breed-placeholder { width: 100%; aspect-ratio: 4/3; background: var(--sage-100); }
    .breed-photo { object-fit: contain; object-position: center; }
    figcaption { padding: 9px 14px; font-size: .67rem; color: var(--muted); line-height: 1.5; overflow-wrap: anywhere; }
    figcaption a { text-decoration: underline; }
    .breed-placeholder { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 5px; color: var(--sage-800); background: radial-gradient(ellipse at 25% 25%, var(--paper), transparent 65%), var(--sage-100); }
    .breed-placeholder span { font-weight: 750; font-size: .8rem; }
    .breed-placeholder small { color: var(--muted); font-size: .7rem; }
  `
})
export class BreedImageComponent {
  readonly image = input<BreedImage | null>(null);
  readonly name = input.required<string>();
  readonly detail = input(false);
  readonly failedUrl = signal('');
  readonly safeUrl = safeExternalUrl;
  readonly url = computed(() => {
    const image = this.image();
    return safeExternalUrl(image?.mediumUrl) || safeExternalUrl(image?.thumbUrl) ||
      (this.detail() ? safeExternalUrl(image?.largeUrl) : null);
  });
}
