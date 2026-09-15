export const ratingFilters = [
  { key: 'trainabilityMin', label: 'Trainability', group: 'Lifestyle & Personality', direction: 'min' },
  { key: 'barkingMax', label: 'Barking', group: 'Lifestyle & Personality', direction: 'max' },
  { key: 'goodWithChildrenMin', label: 'Good with children', group: 'Family & Social', direction: 'min' },
  { key: 'goodWithDogsMin', label: 'Good with dogs', group: 'Family & Social', direction: 'min' },
  { key: 'goodWithStrangersMin', label: 'Good with strangers', group: 'Family & Social', direction: 'min' },
  { key: 'apartmentFriendlyMin', label: 'Apartment friendly', group: 'Family & Social', direction: 'min' },
  { key: 'groomingMax', label: 'Grooming', group: 'Care & Coat', direction: 'max' },
  { key: 'sheddingMax', label: 'Shedding', group: 'Care & Coat', direction: 'max' },
  { key: 'droolingMax', label: 'Drooling', group: 'Care & Coat', direction: 'max' }
] as const;
export const facets = [
  { key: 'breedGroupIds', label: 'Breed group', group: 'Breed Details', searchable: false },
  { key: 'coatLengths', label: 'Coat length', group: 'Care & Coat', searchable: false },
  { key: 'coatTypes', label: 'Coat type', group: 'Care & Coat', searchable: false },
  { key: 'coatColors', label: 'Coat colors', group: 'Care & Coat', searchable: true },
  { key: 'temperaments', label: 'Temperament (all selected traits)', group: 'Lifestyle & Personality', searchable: true },
  { key: 'recognizedBy', label: 'Recognition', group: 'Breed Details', searchable: false },
  { key: 'originCountries', label: 'Origin country', group: 'More filters', searchable: true }
] as const;
export const ranges = [
  { minKey: 'energyMin', maxKey: 'energyMax', label: 'Energy', group: 'Lifestyle & Personality', metadata: 'traitScale', factor: 1, unit: '', step: 1 },
  { minKey: 'exerciseMinMinutes', maxKey: 'exerciseMaxMinutes', label: 'Daily exercise', group: 'Size & Exercise', metadata: 'exerciseMinutes', factor: 1, unit: 'min/day', step: 5 },
  { minKey: 'adultWeightMinKg', maxKey: 'adultWeightMaxKg', label: 'Adult weight', group: 'Size & Exercise', metadata: 'adultWeightKg', factor: 2.2046226218, unit: 'lb', step: 1 },
  { minKey: 'adultHeightMinCm', maxKey: 'adultHeightMaxCm', label: 'Adult height', group: 'More filters', metadata: 'adultHeightCm', factor: 1 / 2.54, unit: 'in', step: 1 }
] as const;
export type RatingKey = typeof ratingFilters[number]['key'];
export type FacetKey = typeof facets[number]['key'];
export type NumericKey = RatingKey | typeof ranges[number]['minKey' | 'maxKey'] | 'minimumLifeMaxYears';
export type AdvancedFilters = Partial<Record<NumericKey, number>> & Partial<Record<FacetKey, string[]>> & { hypoallergenicOnly?: boolean };
export interface DogipediaBreedSearchState extends AdvancedFilters { search: string; page: number; pageSize: number; sort: 'name' }
export interface FilterRange { min: number | null; max: number | null }
export interface FilterOptions {
  traitScale: FilterRange; exerciseMinutes: FilterRange; lifeMaxYears: FilterRange;
  adultWeightKg: FilterRange; adultHeightCm: FilterRange;
  breedGroups: { id: string; name: string }[];
  coatTypes: string[]; coatLengths: string[]; coatColors: string[]; temperaments: string[]; recognizedBy: string[]; originCountries: string[];
}
export const numericKeys: NumericKey[] = [...ratingFilters.map(f => f.key), ...ranges.flatMap(r => [r.minKey, r.maxKey]), 'minimumLifeMaxYears'];
export function searchState(params: { get(key: string): string | null; getAll(key: string): string[] }): DogipediaBreedSearchState {
  const page = Number(params.get('page') || 1), pageSize = Number(params.get('pageSize') || 24);
  const validPageSize = Number.isInteger(pageSize) && pageSize > 0 && pageSize <= 100 ? pageSize : 24;
  const state: DogipediaBreedSearchState = { search: (params.get('search') || '').trim().slice(0, 100),
    page: Number.isSafeInteger(page) && page > 0 && page <= Math.floor(2147483647 / validPageSize) ? page : 1,
    pageSize: validPageSize, sort: 'name' };
  for (const key of numericKeys) {
    const raw = params.get(key), value = Number(raw);
    const rating = key === 'energyMin' || key === 'energyMax' || ratingFilters.some(f => f.key === key);
    if (raw !== null && raw.trim() && Number.isFinite(value) && value >= 0 && (!rating || (Number.isInteger(value) && value >= 1 && value <= 5)) &&
      (!key.startsWith('exercise') || Number.isInteger(value))) state[key] = value;
  }
  for (const r of ranges) if (state[r.minKey] !== undefined && state[r.maxKey] !== undefined && state[r.minKey]! > state[r.maxKey]!) {
    delete state[r.minKey]; delete state[r.maxKey];
  }
  for (const f of facets) {
    const values = [...new Set(params.getAll(f.key).map(v => v.trim().toLowerCase()).filter(Boolean))];
    if (values.length) state[f.key] = values;
  }
  if (params.get('hypoallergenicOnly') === 'true') state.hypoallergenicOnly = true;
  return state;
}
export function searchParams(state: DogipediaBreedSearchState): Record<string, string | number | boolean | string[]> {
  const params: Record<string, string | number | boolean | string[]> = { search: state.search, page: state.page };
  if (state.pageSize !== 24) params['pageSize'] = state.pageSize;
  for (const key of numericKeys) if (state[key] !== undefined) params[key] = state[key]!;
  for (const f of facets) if (state[f.key]?.length) params[f.key] = state[f.key]!;
  if (state.hypoallergenicOnly) params['hypoallergenicOnly'] = true;
  return params;
}
export function clearFilters(state: DogipediaBreedSearchState): DogipediaBreedSearchState {
  return { search: state.search, page: 1, pageSize: state.pageSize, sort: state.sort };
}
export function filterChips(state: AdvancedFilters, options: FilterOptions | null) {
  const chips: { id: string; label: string; keys: (keyof AdvancedFilters)[]; value?: string }[] = [];
  for (const f of ratingFilters) if (state[f.key] !== undefined) chips.push({ id: f.key, keys: [f.key], label: `${f.label} ${f.direction === 'min' ? state[f.key] + '+' : '\u2264 ' + state[f.key]}` });
  for (const r of ranges) {
    const min = state[r.minKey], max = state[r.maxKey];
    const display = (n: number) => Number((n * r.factor).toFixed(1));
    if (min !== undefined || max !== undefined) chips.push({ id: r.minKey, keys: [r.minKey, r.maxKey],
      label: `${r.label} ${min === undefined ? '\u2264 ' + display(max!) : max === undefined ? '\u2265 ' + display(min) : display(min) + '\u2013' + display(max)} ${r.unit}`.trim() });
  }
  if (state.minimumLifeMaxYears !== undefined) chips.push({ id: 'life', keys: ['minimumLifeMaxYears'], label: `Can typically live to ${state.minimumLifeMaxYears}+ years` });
  if (state.hypoallergenicOnly) chips.push({ id: 'hypoallergenic', keys: ['hypoallergenicOnly'], label: 'Hypoallergenic' });
  for (const f of facets) for (const value of state[f.key] || []) chips.push({ id: f.key + value, keys: [f.key], value,
    label: `${f.label}: ${f.key === 'breedGroupIds' ? options?.breedGroups.find(g => g.id === value)?.name || 'Selected breed group' : value}` });
  return chips;
}
