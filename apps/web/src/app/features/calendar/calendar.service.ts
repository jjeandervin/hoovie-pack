import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { CalendarEvent, EventPage, EventValues } from './calendar.models';
import { FileReference } from '../../core/models';
@Injectable({ providedIn: 'root' })
export class CalendarService {
  private http = inject(HttpClient); private config = inject(RuntimeConfigService);
  private url(family: string, id?: string) { return `${this.config.apiBaseUrl}/families/${family}/calendar-events${id ? '/' + id : ''}`; }
  list(family: string, query: Record<string, string | number>) { return this.http.get<EventPage>(this.url(family), { params: new HttpParams({ fromObject: query }) }); }
  get(family: string, id: string) { return this.http.get<CalendarEvent>(this.url(family, id)); }
  save(family: string, values: EventValues, id?: string) { return id ? this.http.put<CalendarEvent>(this.url(family, id), values) : this.http.post<CalendarEvent>(this.url(family), values); }
  delete(family: string, id: string) { return this.http.delete<void>(this.url(family, id)); }
  addPhoto(family: string, id: string, file: FileReference) { return this.http.post<CalendarEvent>(this.url(family, id) + '/photos', file); }
  removePhoto(family: string, id: string, photo: string) { return this.http.delete<CalendarEvent>(this.url(family, id) + '/photos/' + photo); }
  cover(family: string, id: string, photo: string) { return this.http.put<CalendarEvent>(this.url(family, id) + '/photos/' + photo + '/cover', {}); }
}
