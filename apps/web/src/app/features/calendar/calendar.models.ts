export const eventTypes = ['Family', 'Birthday', 'Holiday', 'Vacation', 'School', 'Dog / Pet', 'Celebration', 'Appointment', 'Memory', 'Other'];
export interface CalendarPhoto { id: string; fileId: string; url: string; isCover: boolean; sortOrder: number; }
export interface EventValues {
  title: string; description: string | null; eventType: string; startDate: string; endDate: string | null;
  isAllDay: boolean; startTime: string | null; endTime: string | null; timeZoneId: string | null; location: string | null;
}
export interface CalendarEvent extends EventValues {
  id: string; familyId: string; createdBy: { id: string; displayName: string }; createdAtUtc: string;
  updatedAtUtc: string; canManage: boolean; photos: CalendarPhoto[];
}
export interface CalendarItem {
  id: string; sourceType: 'calendarEvent' | 'memberBirthday' | 'dogBirthday';
  title: string; eventType: string; startDate: string; endDate: string | null;
  isAllDay: boolean; startTime: string | null; calendarEventId: string | null;
  memberId: string | null; dogId: string | null; imageUrl: string | null;
  isEditable: boolean; photos: CalendarPhoto[];
}
export function calendarItemLink(item: CalendarItem): string[] {
  if (item.sourceType === 'memberBirthday') return ['/members', item.memberId!];
  if (item.sourceType === 'dogBirthday') return ['/dogs', item.dogId!];
  return ['/tools/calendar/events', item.calendarEventId || item.id];
}
export interface EventPage { items: CalendarItem[]; totalCount: number; totalPages: number; page: number; }
// Parse date-only values as local dates, avoiding UTC shifts and the Date constructor's 1900 offset for years 1?99.
export function localDate(value: string): Date {
  const [y, m, d] = value.split('-').map(Number); const date = new Date(0);
  date.setFullYear(y, m - 1, d); date.setHours(12, 0, 0, 0); return date;
}
export function dateKey(date: Date): string {
  return `${String(date.getFullYear()).padStart(4, '0')}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}
export function occursOn(event: Pick<EventValues, 'startDate' | 'endDate'>, date: string): boolean { return event.startDate <= date && (event.endDate || event.startDate) >= date; }
export function monthDays(month: string): string[] {
  const date = localDate(month + '-01'); const days: string[] = [];
  const offset = date.getDay(); for (let i = 0; i < offset; i++) days.push('');
  while (dateKey(date).slice(0, 7) === month) { days.push(dateKey(date)); date.setDate(date.getDate() + 1); }
  while (days.length % 7) days.push('');
  return days;
}
