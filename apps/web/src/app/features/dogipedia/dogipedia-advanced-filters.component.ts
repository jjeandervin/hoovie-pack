import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { AdvancedFilters, FacetKey, FilterOptions, NumericKey, facets, ranges, ratingFilters } from './dogipedia-search';

@Component({
  selector: 'hp-dogipedia-advanced-filters', standalone: true,
  templateUrl: './dogipedia-advanced-filters.component.html', styleUrl: './dogipedia-advanced-filters.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DogipediaAdvancedFiltersComponent {
  readonly state = input.required<AdvancedFilters>();
  readonly options = input.required<FilterOptions>();
  readonly filtersChange = output<AdvancedFilters>();
  readonly groups = ['Lifestyle & Personality', 'Family & Social', 'Care & Coat', 'Size & Exercise', 'Breed Details', 'More filters'];
  readonly ratings = ratingFilters;
  readonly facets = facets;
  readonly ranges = ranges;
  readonly terms: Partial<Record<FacetKey, string>> = {};
  setNumber(key: NumericKey, value: string, any?: number): void {
    const number = Number(value);
    this.filtersChange.emit({ ...this.state(), [key]: value === '' || number === any ? undefined : number });
  }
  toggleHypoallergenic(value: boolean): void { this.filtersChange.emit({ ...this.state(), hypoallergenicOnly: value || undefined }); }
  toggle(key: FacetKey, value: string): void {
    const selected = this.state()[key] || [];
    this.filtersChange.emit({ ...this.state(), [key]: selected.includes(value) ? selected.filter(v => v !== value) : [...selected, value] });
  }
  choices(key: FacetKey, searchable: boolean) {
    const options = this.options();
    const all = key === 'breedGroupIds' ? options.breedGroups.map(g => ({ value: g.id, label: g.name })) : options[key].map(label => ({ value: label.trim().toLowerCase(), label }));
    const term = (this.terms[key] || '').trim().toLowerCase();
    const matches = all.filter(o => !this.state()[key]?.includes(o.value) && o.label.toLowerCase().includes(term));
    return searchable ? matches.slice(0, 12) : all;
  }
  bounds(r: typeof ranges[number]): [number, number] {
    const range = this.options()[r.metadata];
    return [Math.floor((range.min ?? 0) * r.factor / r.step) * r.step, Math.ceil((range.max ?? 0) * r.factor / r.step) * r.step];
  }
  value(r: typeof ranges[number], upper: boolean): number {
    const value = this.state()[upper ? r.maxKey : r.minKey];
    return value === undefined ? this.bounds(r)[upper ? 1 : 0] : Number((value * r.factor).toFixed(2));
  }
  setRange(r: typeof ranges[number], upper: boolean, input: HTMLInputElement): void {
    const bounds = this.bounds(r);
    const value = upper ? Math.max(Number(input.value), this.value(r, false)) : Math.min(Number(input.value), this.value(r, true));
    input.value = String(value);
    this.filtersChange.emit({ ...this.state(), [upper ? r.maxKey : r.minKey]: value === bounds[upper ? 1 : 0] ? undefined : Number((value / r.factor).toFixed(6)) });
  }
  rangeLabel(r: typeof ranges[number]): string {
    return this.state()[r.minKey] === undefined && this.state()[r.maxKey] === undefined ? 'Any' : `${this.value(r, false)}\u2013${this.value(r, true)} ${r.unit}`;
  }
}
