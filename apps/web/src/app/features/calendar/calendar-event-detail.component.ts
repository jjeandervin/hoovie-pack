import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { FileReference } from '../../core/models';
import { apiErrorMessage } from '../../core/api-error';
import { AuthImageDirective } from '../../shared/auth-image.directive';
import { ImageUploaderComponent } from '../../shared/image-uploader.component';
import { CalendarService } from './calendar.service';
import { CalendarEvent, CalendarPhoto, localDate } from './calendar.models';
@Component({ selector: 'hp-calendar-event-detail', standalone: true,
  imports: [DatePipe, RouterLink, AuthImageDirective, ImageUploaderComponent],
  templateUrl: './calendar-event-detail.component.html', styleUrl: './calendar.css', changeDetection: ChangeDetectionStrategy.OnPush })
export class CalendarEventDetailComponent {
  readonly families = inject(ActiveFamilyService); private api = inject(CalendarService); private uploads = inject(ApiService);
  private route = inject(ActivatedRoute); private params = toSignal(this.route.paramMap); private router = inject(Router);
  readonly event = signal<CalendarEvent | null>(null); readonly loading = signal(false); readonly busy = signal(false); readonly error = signal('');
  readonly selectedPhoto = signal<CalendarPhoto | null>(null); readonly photos = signal<File[]>([]); readonly uploaderKey = signal(0);
  readonly confirmDelete = signal(false); readonly localDate = localDate; readonly refresh = signal(0);
  private completed = new Set<File>(); private references = new Map<File, FileReference>();
  constructor() {
    effect(onCleanup => {
      const family = this.families.activeId(), id = this.params()?.get('eventId'); this.refresh();
      this.event.set(null); this.error.set(''); this.selectedPhoto.set(null); this.confirmDelete.set(false); this.photos.set([]); this.completed.clear(); this.references.clear();
      this.loading.set(!!family && !!id); if (!family || !id) return;
      const sub = this.api.get(family, id).subscribe({ next: e => { this.event.set(e); this.loading.set(false); }, error: err => { this.error.set(apiErrorMessage(err, 'Event not found.')); this.loading.set(false); } });
      onCleanup(() => sub.unsubscribe());
    });
  }
  async action(kind: 'delete' | 'cover' | 'remove', photo?: CalendarPhoto) {
    const e = this.event(); if (!e || !e.canManage || this.busy()) return;
    this.busy.set(true); this.error.set('');
    try {
      if (kind === 'delete') { await firstValueFrom(this.api.delete(e.familyId, e.id)); if (this.event()?.id === e.id) await this.router.navigateByUrl('/tools/calendar'); }
      else { const updated = await firstValueFrom(kind === 'cover' ? this.api.cover(e.familyId, e.id, photo!.id) : this.api.removePhoto(e.familyId, e.id, photo!.id)); if (this.event()?.id === e.id) this.event.set(updated); }
    } catch (err) { this.error.set(apiErrorMessage(err, 'Could not update this event.')); } finally { this.busy.set(false); }
  }
  async upload() {
    const e = this.event(); if (!e || !e.canManage || this.busy()) return;
    this.busy.set(true); this.error.set('');
    try {
      for (const file of this.photos()) {
        if (this.completed.has(file)) continue;
        const reference = this.references.get(file) ?? await firstValueFrom(this.uploads.uploadFile(file, 'calendarPhoto', e.familyId));
        this.references.set(file, reference);
        const updated = await firstValueFrom(this.api.addPhoto(e.familyId, e.id, reference)); this.completed.add(file);
        if (this.event()?.id !== e.id) return; this.event.set(updated);
      }
      this.photos.set([]); this.completed.clear(); this.references.clear(); this.uploaderKey.update(v => v + 1);
    } catch (err) { this.error.set(apiErrorMessage(err, 'Some photos could not be attached. Retry to upload the remaining photos.')); } finally { this.busy.set(false); }
  }
}
