import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthImageDirective } from '../../shared/auth-image.directive';
import { Subscription } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { apiErrorMessage } from '../../core/api-error';
import { CalendarService } from './calendar.service';
import { CalendarItem, calendarItemLink, dateKey, localDate, monthDays, occursOn } from './calendar.models';

@Component({
  selector: 'hp-calendar-page', standalone: true, imports: [RouterLink, FormsModule, DatePipe, AuthImageDirective],
  templateUrl: './calendar-page.component.html', styleUrl: './calendar.css', changeDetection: ChangeDetectionStrategy.OnPush
})
export class CalendarPageComponent {
  readonly families = inject(ActiveFamilyService); private api = inject(CalendarService);
  readonly today = dateKey(new Date()); readonly month = signal(this.today.slice(0, 7));
  readonly selected = signal(this.today); readonly view = signal<'calendar' | 'timeline'>('calendar');
  readonly direction = signal('upcoming'); readonly page = signal(1); readonly totalPages = signal(0);
  readonly events = signal<CalendarItem[]>([]); readonly loading = signal(false); readonly error = signal('');
  readonly refresh = signal(0); readonly days = computed(() => monthDays(this.month()));
  readonly selectedEvents = computed(() => this.events().filter(e => occursOn(e, this.selected())));
  readonly groups = computed(() => {
    const groups = new Map<string, CalendarItem[]>();
    for (const e of this.events()) { const key = e.startDate.slice(0, 7); groups.set(key, [...(groups.get(key) ?? []), e]); }
    return [...groups].map(([month, events]) => ({ month, events }));
  });
  readonly localDate = localDate;
  readonly itemLink = calendarItemLink;
  itemIcon(item: CalendarItem) { return item.sourceType === 'dogBirthday' ? '\uD83D\uDC3E' : item.sourceType === 'memberBirthday' ? '\uD83C\uDF82' : ''; }
  constructor() {
    effect(onCleanup => {
      const family = this.families.activeId(), month = this.month(), view = this.view(), direction = this.direction(), page = this.page(); this.refresh();
      this.events.set([]); this.error.set(''); this.loading.set(!!family); this.totalPages.set(0);
      if (!family) return;
      const subscriptions = new Subscription(); let cancelled = false;
      const last = localDate(month + '-01'); last.setMonth(last.getMonth() + 1, 0);
      const query: Record<string, string | number> = view === 'calendar'
        ? { startDate: month + '-01', endDate: dateKey(last), pageSize: 100 }
        : { ...(direction === 'upcoming' ? { startDate: this.today } : { endDate: this.yesterday() }), direction, pageSize: 30 };
      const load = (next: number) => subscriptions.add(this.api.list(family, { ...query, page: next }).subscribe({
        next: result => {
          if (cancelled) return;
          this.events.update(events => [...events, ...result.items]); this.totalPages.set(result.totalPages);
          if (view === 'calendar' && next < result.totalPages) load(next + 1); else this.loading.set(false);
        }, error: error => { this.error.set(apiErrorMessage(error, 'Could not load calendar events.')); this.loading.set(false); }
      }));
      load(view === 'calendar' ? 1 : page);
      onCleanup(() => { cancelled = true; subscriptions.unsubscribe(); });
    });
  }
  private yesterday() { const date = localDate(this.today); date.setDate(date.getDate() - 1); return dateKey(date); }
  onDay(day: string) { return this.events().filter(e => occursOn(e, day)); }
  changeMonth(value: string) {
    if (!/^\d{4}-\d{2}$/.test(value) || Number(value.slice(0, 4)) < 1 || Number(value.slice(5)) < 1 || Number(value.slice(5)) > 12) return;
    this.month.set(value); this.selected.set(value + '-01');
  }
  move(amount: number) { const date = localDate(this.month() + '-01'); date.setMonth(date.getMonth() + amount); if (date.getFullYear() >= 1 && date.getFullYear() <= 9999) this.changeMonth(dateKey(date).slice(0, 7)); }
  goToday() { this.month.set(this.today.slice(0, 7)); this.selected.set(this.today); }
  switchView(value: 'calendar' | 'timeline') { this.view.set(value); this.page.set(1); }
  switchDirection(value: string) { this.direction.set(value); this.page.set(1); }
}
