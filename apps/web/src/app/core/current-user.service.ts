import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { UserProfile } from './models';

@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  private readonly profileSignal = signal<UserProfile | null>(null);
  private readonly loadingSignal = signal(false);
  private loadPromise?: Promise<UserProfile>;

  readonly profile = this.profileSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();

  constructor(private readonly api: ApiService) {}

  load(force = false): Promise<UserProfile> {
    if (this.loadPromise && !force) return this.loadPromise;
    this.loadingSignal.set(true);
    const pending = firstValueFrom(this.api.getMe())
      .then((profile) => {
        this.profileSignal.set(profile);
        return profile;
      })
      .catch((error) => {
        // A failed load must not poison later retries or clear a newer load.
        if (this.loadPromise === pending) this.loadPromise = undefined;
        throw error;
      })
      .finally(() => {
        if (!this.loadPromise || this.loadPromise === pending) this.loadingSignal.set(false);
      });
    this.loadPromise = pending;
    return pending;
  }

  set(profile: UserProfile): void {
    this.profileSignal.set(profile);
  }
}
