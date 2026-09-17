import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { FileReference } from '../../core/models';
import { apiErrorMessage } from '../../core/api-error';
import { ImageUploaderComponent } from '../../shared/image-uploader.component';
import { CalendarService } from './calendar.service';
import { dateKey, eventTypes } from './calendar.models';
@Component({
  selector: 'hp-calendar-event-form', standalone: true,
  imports: [ReactiveFormsModule, RouterLink, ImageUploaderComponent],
  templateUrl: './calendar-event-form.component.html', styleUrl: './calendar.css', changeDetection: ChangeDetectionStrategy.OnPush
})
export class CalendarEventFormComponent {
  readonly families = inject(ActiveFamilyService); private api = inject(CalendarService); private uploads = inject(ApiService);
  private route = inject(ActivatedRoute); private router = inject(Router);
  readonly eventTypes = eventTypes; readonly loading = signal(false); readonly saving = signal(false); readonly error = signal('');
  readonly canManage = signal(true); readonly photos = signal<File[]>([]); readonly savedId = signal<string | undefined>(undefined);
  readonly editing = !!this.route.snapshot.paramMap.get('eventId');
  private completed = new Set<File>(); private references = new Map<File, FileReference>();
  readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    startDate: new FormControl(this.route.snapshot.queryParamMap.get('date') || dateKey(new Date()), { nonNullable: true, validators: Validators.required }),
    endDate: new FormControl('', { nonNullable: true }), isAllDay: new FormControl(true, { nonNullable: true }),
    startTime: new FormControl('', { nonNullable: true }), endTime: new FormControl('', { nonNullable: true }),
    timeZoneId: new FormControl(Intl.DateTimeFormat().resolvedOptions().timeZone, { nonNullable: true }),
    eventType: new FormControl('Family', { nonNullable: true }), location: new FormControl('', { nonNullable: true, validators: Validators.maxLength(500) }),
    description: new FormControl('', { nonNullable: true })
  });
  constructor() {
    effect(onCleanup => {
      const family = this.families.activeId(); const id = this.route.snapshot.paramMap.get('eventId');
      this.savedId.set(undefined); this.completed.clear(); this.references.clear(); this.error.set(''); this.canManage.set(!id);
      if (!family || !id) return;
      this.loading.set(true);
      const sub = this.api.get(family, id).subscribe({ next: e => {
        this.form.patchValue({ ...e, endDate: e.endDate ?? '', startTime: e.startTime ?? '', endTime: e.endTime ?? '', location: e.location ?? '', description: e.description ?? '', timeZoneId: e.timeZoneId ?? Intl.DateTimeFormat().resolvedOptions().timeZone });
        this.savedId.set(e.id); this.canManage.set(e.canManage); this.loading.set(false);
      }, error: error => { this.error.set(apiErrorMessage(error, 'Could not load this event.')); this.loading.set(false); } });
      onCleanup(() => sub.unsubscribe());
    });
  }
  async save() {
    const family = this.families.activeId(); if (!family || this.saving() || !this.canManage()) return;
    const v = this.form.getRawValue(); this.error.set('');
    if (this.form.invalid || !v.title.trim()) { this.form.markAllAsTouched(); this.error.set('Enter a title (up to 200 characters), a start date, and a location of up to 500 characters.'); return; }
    if (v.endDate && v.endDate < v.startDate) { this.error.set('End date cannot precede start date.'); return; }
    if (!v.isAllDay && (!v.startTime || ((v.endDate || v.startDate) === v.startDate && v.endTime && v.endTime < v.startTime))) { this.error.set('Enter a start time and an end time that is not before it.'); return; }
    this.saving.set(true);
    try {
      const e = await firstValueFrom(this.api.save(family, { ...v, endDate: v.endDate || null, startTime: v.isAllDay ? null : v.startTime, endTime: v.isAllDay ? null : v.endTime || null, timeZoneId: v.isAllDay ? null : v.timeZoneId }, this.savedId()));
      if (family !== this.families.activeId()) return;
      this.savedId.set(e.id);
      for (const photo of this.photos()) {
        if (this.completed.has(photo)) continue;
        const reference = this.references.get(photo) ?? await firstValueFrom(this.uploads.uploadFile(photo, 'calendarPhoto', family));
        this.references.set(photo, reference);
        await firstValueFrom(this.api.addPhoto(family, e.id, reference)); this.completed.add(photo);
        if (family !== this.families.activeId()) return;
      }
      await this.router.navigate(['/tools/calendar/events', e.id]);
    } catch (error) { this.error.set(apiErrorMessage(error, this.savedId() ? 'Event saved, but some photos could not be attached. Save again to retry the remaining photos.' : 'Could not save this event.')); }
    finally { this.saving.set(false); }
  }
}
