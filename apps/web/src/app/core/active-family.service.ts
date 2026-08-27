import { Injectable, computed, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { FamilySummary } from './models';

const ACTIVE_FAMILY_KEY = 'hooviepack.activeFamilyId';

@Injectable({ providedIn: 'root' })
export class ActiveFamilyService {
  private readonly familiesSignal = signal<FamilySummary[]>([]);
  private readonly activeIdSignal = signal<string | null>(null);
  private readonly loadingSignal = signal(false);
  private loadPromise?: Promise<FamilySummary[]>;

  readonly families = this.familiesSignal.asReadonly();
  readonly activeId = this.activeIdSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly activeFamily = computed(
    () => this.familiesSignal().find((family) => family.id === this.activeIdSignal()) ?? null
  );
  readonly canManage = computed(() => ['Owner', 'Admin'].includes(this.activeFamily()?.role ?? ''));

  constructor(private readonly api: ApiService) {}

  load(force = false): Promise<FamilySummary[]> {
    if (this.loadPromise && !force) return this.loadPromise;
    this.loadingSignal.set(true);
    this.loadPromise = firstValueFrom(this.api.listFamilies())
      .then((families) => {
        this.familiesSignal.set(families);
        const savedId = this.readSavedId();
        const next = families.find((family) => family.id === savedId) ?? families[0] ?? null;
        this.select(next?.id ?? null);
        return families;
      })
      .finally(() => this.loadingSignal.set(false));
    return this.loadPromise;
  }

  select(familyId: string | null): void {
    if (familyId && !this.familiesSignal().some((family) => family.id === familyId)) return;
    this.activeIdSignal.set(familyId);
    try {
      if (familyId) localStorage.setItem(ACTIVE_FAMILY_KEY, familyId);
      else localStorage.removeItem(ACTIVE_FAMILY_KEY);
    } catch {
      // Storage can be unavailable in hardened browsers; the in-memory selection still works.
    }
  }

  upsert(family: FamilySummary): void {
    this.familiesSignal.update((families) => {
      const index = families.findIndex((entry) => entry.id === family.id);
      return index < 0
        ? [...families, family]
        : families.map((entry) => (entry.id === family.id ? family : entry));
    });
    this.select(family.id);
  }

  clear(): void {
    this.familiesSignal.set([]);
    this.select(null);
    this.loadPromise = undefined;
  }

  private readSavedId(): string | null {
    try {
      return localStorage.getItem(ACTIVE_FAMILY_KEY);
    } catch {
      return null;
    }
  }
}
