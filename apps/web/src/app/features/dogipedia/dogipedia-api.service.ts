import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { BreedDetail, BreedPage } from './dogipedia.models';

@Injectable({ providedIn: 'root' })
export class DogipediaApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(RuntimeConfigService);

  list(search: string, page: number, pageSize = 24) {
    const params = new HttpParams().set('search', search).set('page', page).set('pageSize', pageSize);
    return this.http.get<BreedPage>(`${this.config.apiBaseUrl}/dogipedia/breeds`, { params });
  }

  get(id: string) {
    return this.http.get<BreedDetail>(`${this.config.apiBaseUrl}/dogipedia/breeds/${encodeURIComponent(id)}`);
  }
}
