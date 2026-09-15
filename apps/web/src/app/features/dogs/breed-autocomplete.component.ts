import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { catchError, map, of, startWith, switchMap, timer } from 'rxjs';
import { DogipediaApiService } from '../dogipedia/dogipedia-api.service';

@Component({
  selector: 'hp-breed-autocomplete', standalone: true,
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="breed-autocomplete">
      <input [id]="inputId()" [formControl]="control()" maxlength="100" autocomplete="off"
        placeholder="Start typing a breed…" role="combobox" aria-autocomplete="list"
        [attr.aria-expanded]="expanded()" [attr.aria-controls]="expanded() ? inputId() + '-options' : null"
        [attr.aria-activedescendant]="expanded() && activeIndex() >= 0 ? inputId() + '-option-' + activeIndex() : null"
        [attr.aria-describedby]="inputId() + '-hint'"
        (focus)="open.set(true)" (input)="open.set(true)" (blur)="close()" (keydown)="onKeydown($event)">
      @if (expanded()) {
        <ul class="breed-options" [id]="inputId() + '-options'" role="listbox" aria-label="Suggested breeds">
          @for (name of suggestions(); track name; let index = $index) {
            <li role="option" [id]="inputId() + '-option-' + index" [attr.aria-selected]="activeIndex() === index"
              (pointerdown)="$event.preventDefault()" (click)="choose(name)">{{ name }}</li>
          }
        </ul>
      }
    </div>
    <small [id]="inputId() + '-hint'">Choose a suggested breed or enter your own, including a mix.</small>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .breed-autocomplete { position: relative; }
    .breed-options { position: absolute; z-index: 10; top: calc(100% + 5px); left: 0; right: 0; max-height: 260px; overflow-y: auto; overscroll-behavior: contain; margin: 0; padding: 5px; list-style: none; border: 1px solid var(--line); border-radius: 12px; background: var(--paper-bright); box-shadow: var(--shadow-md); }
    .breed-options li { padding: 12px 10px; min-height: 44px; border-radius: 7px; color: var(--sage-900); font-size: .85rem; cursor: pointer; overflow-wrap: anywhere; }
    .breed-options li:hover, .breed-options li[aria-selected="true"] { background: var(--sage-100); }
    small { display: block; margin-top: 6px; color: var(--muted-light); font-size: .74rem; }
  `
})
export class BreedAutocompleteComponent implements OnInit {
  readonly control = input.required<FormControl<string>>();
  readonly inputId = input('dog-breed');
  readonly suggestions = signal<string[]>([]);
  readonly open = signal(false);
  readonly activeIndex = signal(-1);
  readonly expanded = computed(() => this.open() && this.suggestions().length > 0);
  private readonly api = inject(DogipediaApiService);
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.control().valueChanges.pipe(
      startWith(this.control().value),
      map(value => value.trim()),
      // Cancel both a pending debounce and an older request as soon as typing changes.
      switchMap(search => {
        this.suggestions.set([]);
        this.activeIndex.set(-1);
        if (!search || search.length > 100) return of<string[]>([]);
        return timer(300).pipe(switchMap(() => this.api.list(search, 1, 8)),
          map(page => [...new Set(page.items.map(breed => breed.name))].filter(name => name.length <= 100)),
          // Suggestions are optional: a catalog outage must not block adding a dog.
          catchError(() => of<string[]>([])));
      }), takeUntilDestroyed(this.destroyRef)
    ).subscribe(names => this.suggestions.set(names));
  }

  choose(name: string): void {
    this.control().setValue(name, { emitEvent: false });
    this.control().markAsDirty();
    this.close();
  }

  close(): void { this.open.set(false); this.activeIndex.set(-1); }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') { if (this.expanded()) event.preventDefault(); this.close(); return; }
    if (event.key === 'Tab') { this.close(); return; }
    if (event.key === 'Enter' && this.expanded() && this.activeIndex() >= 0) {
      event.preventDefault();
      this.choose(this.suggestions()[this.activeIndex()]);
      return;
    }
    if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && this.suggestions().length) {
      event.preventDefault();
      this.open.set(true);
      const next = event.key === 'ArrowDown' ? Math.min(this.activeIndex() + 1, this.suggestions().length - 1)
        : this.activeIndex() <= 0 ? this.suggestions().length - 1 : this.activeIndex() - 1;
      this.activeIndex.set(next);
      document.getElementById(`${this.inputId()}-option-${next}`)?.scrollIntoView({ block: 'nearest' });
    }
  }
}
