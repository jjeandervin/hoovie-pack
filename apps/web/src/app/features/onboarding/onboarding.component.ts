import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize, firstValueFrom } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { CurrentUserService } from '../../core/current-user.service';
import { FamilySummary } from '../../core/models';
import { ImageUploaderComponent } from '../../shared/image-uploader.component';

@Component({
  selector: 'hp-onboarding',
  standalone: true,
  imports: [ReactiveFormsModule, ImageUploaderComponent],
  templateUrl: './onboarding.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class OnboardingComponent implements OnInit {
  readonly mode = signal<'create' | 'join'>('create');
  readonly step = signal<'family' | 'profile'>('family');
  readonly submitting = signal(false);
  readonly error = signal('');
  readonly pendingFamily = signal<FamilySummary | null>(null);
  readonly profilePhoto = signal<File | undefined>(undefined);

  readonly createForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(80)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(300)] })
  });
  readonly joinForm = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(4), Validators.maxLength(128)] })
  });
  readonly profileForm = new FormGroup({
    displayName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(80)] }),
    bio: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(280)] })
  });

  constructor(
    private readonly api: ApiService,
    private readonly families: ActiveFamilyService,
    private readonly currentUser: CurrentUserService,
    private readonly route: ActivatedRoute,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    const code = this.route.snapshot.queryParamMap.get('code') || this.extractCode(this.route.snapshot.queryParamMap.get('invite') || '');
    if (code) {
      this.mode.set('join');
      this.joinForm.controls.code.setValue(code);
    }
    void this.currentUser.load().then((profile) => {
      this.profileForm.patchValue({ displayName: profile.displayName || '', bio: profile.bio || '' });
    }).catch(() => undefined);
  }

  createFamily(): void {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }
    this.submitFamily(firstValueFrom(this.api.createFamily(this.createForm.getRawValue())));
  }

  joinFamily(): void {
    if (this.joinForm.invalid) {
      this.joinForm.markAllAsTouched();
      return;
    }
    const code = this.extractCode(this.joinForm.controls.code.value);
    this.submitFamily(firstValueFrom(this.api.joinFamily(code)));
  }

  saveProfile(): void {
    if (this.profileForm.invalid) {
      this.profileForm.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.error.set('');
    const values = this.profileForm.getRawValue();
    this.api.updateMe(values.displayName, values.bio, this.profilePhoto()).pipe(
      finalize(() => this.submitting.set(false))
    ).subscribe({
      next: (profile) => {
        this.currentUser.set(profile);
        this.finish();
      },
      error: (error) => this.error.set(apiErrorMessage(error, 'We could not save your profile.'))
    });
  }

  finish(): void {
    void this.router.navigateByUrl('/feed');
  }

  showError(control: FormControl<string>): boolean {
    return control.invalid && (control.dirty || control.touched);
  }

  private submitFamily(operation: Promise<FamilySummary>): void {
    this.submitting.set(true);
    this.error.set('');
    operation
      .then((family) => {
        this.families.upsert(family);
        this.pendingFamily.set(family);
        this.step.set('profile');
      })
      .catch((error) => this.error.set(apiErrorMessage(error, 'We could not set up that family.')))
      .finally(() => this.submitting.set(false));
  }

  private extractCode(value: string): string {
    const trimmed = value.trim();
    if (!trimmed.includes('/')) return trimmed;
    try {
      const url = new URL(trimmed, window.location.origin);
      return url.searchParams.get('code') || url.pathname.split('/').filter(Boolean).at(-1) || trimmed;
    } catch {
      return trimmed;
    }
  }
}
