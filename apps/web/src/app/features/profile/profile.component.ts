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
  templateUrl: './profile.component.html',
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
