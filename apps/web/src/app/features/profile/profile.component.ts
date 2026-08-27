import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { CurrentUserService } from '../../core/current-user.service';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { ToastService } from '../../core/toast.service';
import { AvatarComponent } from '../../shared/avatar.component';
import { ImageUploaderComponent } from '../../shared/image-uploader.component';
import { UiStateComponent } from '../../shared/ui-state.component';

@Component({
  selector: 'hp-profile',
  standalone: true,
  imports: [ReactiveFormsModule, AvatarComponent, ImageUploaderComponent, UiStateComponent],
  template: `
    <div class="page profile-page">
      <header class="page-heading page-heading--actions">
        <div><p class="eyebrow">Your corner</p><h1>Profile</h1><p>Help your family recognize you and know what’s new in your world.</p></div>
        <button type="button" class="button button--secondary desktop-signout" (click)="auth.logout()">Sign out</button>
      </header>

      @if (loading()) {
        <hp-ui-state kind="loading" heading="Fetching your profile…" [compact]="true" />
      } @else if (loadError()) {
        <hp-ui-state kind="error" heading="We couldn’t open your profile" [message]="loadError()" actionLabel="Try again" (action)="load()" />
      } @else {
        <div class="profile-layout">
          <aside class="profile-summary">
            <div class="profile-summary__cover" aria-hidden="true"><span>●</span><span>●</span><span>●</span></div>
            <hp-avatar [src]="previewUrl() || user.profile()?.avatarUrl" [name]="user.profile()?.displayName || 'You'" [size]="112" />
            <h2>{{ user.profile()?.displayName }}</h2>
            <p>{{ user.profile()?.email }}</p>
            <span class="role-badge">{{ families.activeFamily()?.role || 'Pack member' }}</span>
            <div class="profile-summary__meta">
              <div><strong>{{ families.families().length }}</strong><small>{{ families.families().length === 1 ? 'Family' : 'Families' }}</small></div>
              <div><strong>◇</strong><small>Private</small></div>
            </div>
            @if (user.profile()?.createdAt) { <small class="member-since">Part of HooviePack since {{ memberSince(user.profile()!.createdAt!) }}</small> }
          </aside>

          <form [formGroup]="form" (ngSubmit)="save()" class="profile-form stack-form" novalidate>
            <section class="settings-card">
              <div class="settings-card__heading"><span aria-hidden="true">○</span><div><h2>Profile photo</h2><p>Choose a warm, recognizable photo of you.</p></div></div>
              <hp-image-uploader [maxFiles]="1" [disabled]="saving()" (filesChange)="setAvatar($event[0])" />
            </section>

            <section class="settings-card stack-form">
              <div class="settings-card__heading"><span aria-hidden="true">✎</span><div><h2>About you</h2><p>This is visible only to families you belong to.</p></div></div>
              <div class="field"><label for="profile-name">Display name</label><input id="profile-name" formControlName="displayName" maxlength="80" autocomplete="name">@if (showError('displayName')) { <p class="field-error">Enter a display name between 2 and 80 characters.</p> }</div>
              <div class="field"><label for="profile-email">Email</label><input id="profile-email" [value]="user.profile()?.email || ''" disabled><small>Your email comes from secure sign-in and cannot be changed here.</small></div>
              <div class="field"><label for="profile-bio">Short bio <span>Optional</span></label><textarea id="profile-bio" formControlName="bio" rows="4" maxlength="280" placeholder="Family role, current adventure, or favorite way to spend a Sunday…"></textarea><small>{{ form.controls.bio.value.length }}/280</small></div>
              @if (saveError()) { <div class="alert alert--error" role="alert"><span>!</span><p>{{ saveError() }}</p></div> }
              <div class="form-actions"><button type="submit" class="button button--large" [disabled]="saving() || form.invalid">{{ saving() ? 'Saving your profile…' : 'Save profile' }}</button></div>
            </section>

            <button type="button" class="button button--text mobile-signout danger-text" (click)="auth.logout()">Sign out of HooviePack</button>
          </form>
        </div>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ProfileComponent implements OnInit {
  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly saving = signal(false);
  readonly saveError = signal('');
  readonly avatar = signal<File | undefined>(undefined);
  readonly previewUrl = signal<string | null>(null);
  readonly form = new FormGroup({
    displayName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(80)] }),
    bio: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(280)] })
  });

  constructor(
    readonly user: CurrentUserService,
    readonly families: ActiveFamilyService,
    readonly auth: AuthService,
    private readonly api: ApiService,
    private readonly config: RuntimeConfigService,
    private readonly toasts: ToastService
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    void this.user.load(true).then((profile) => {
      this.form.setValue({ displayName: profile.displayName || '', bio: profile.bio || '' });
    }).catch((error) => this.loadError.set(apiErrorMessage(error, 'Your profile is unavailable right now.')))
      .finally(() => this.loading.set(false));
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.saving.set(true);
    this.saveError.set('');
    this.api.updateMe(value.displayName.trim(), value.bio.trim(), this.avatar()).pipe(
      finalize(() => this.saving.set(false))
    ).subscribe({
      next: (profile) => {
        this.user.set(profile);
        this.toasts.success('Your profile is up to date.');
        this.form.markAsPristine();
      },
      error: (error) => this.saveError.set(apiErrorMessage(error, 'We could not save your profile.'))
    });
  }

  setAvatar(file?: File): void {
    const current = this.previewUrl();
    if (current) URL.revokeObjectURL(current);
    this.avatar.set(file);
    this.previewUrl.set(file ? URL.createObjectURL(file) : null);
  }

  showError(controlName: 'displayName'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.dirty || control.touched);
  }

  memberSince(value: string): string { return new Date(value).toLocaleDateString(undefined, { month: 'long', year: 'numeric' }); }
}
