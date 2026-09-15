import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { DogipediaBreedSearchState, FilterOptions, searchParams } from './dogipedia-search';
import { BreedDetail, BreedPage } from './dogipedia.models';

@Injectable({ providedIn: 'root' })
export class DogipediaApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(RuntimeConfigService);

  list(search: string, page: number, pageSize = 24) {
    const params = new HttpParams().set('search', search).set('page', page).set('pageSize', pageSize);
    return this.http.get<BreedPage>(`${this.config.apiBaseUrl}/dogipedia/breeds`, { params });
  }

  search(state: DogipediaBreedSearchState) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(searchParams(state))) {
      for (const item of Array.isArray(value) ? value : [value]) params = params.append(key, item);
    }
    return this.http.get<BreedPage>(`${this.config.apiBaseUrl}/dogipedia/breeds`, { params });
  }

  filterOptions() {
    return this.http.get<FilterOptions>(`${this.config.apiBaseUrl}/dogipedia/breeds/filter-options`);
  }

  get(id: string) {
    return this.http.get<BreedDetail>(`${this.config.apiBaseUrl}/dogipedia/breeds/${encodeURIComponent(id)}`);
  }
}
