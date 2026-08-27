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
  template: `
    <main id="main-content" class="onboarding-page">
      <header class="onboarding-header">
        <div class="brand" aria-label="HooviePack">
          <span class="brand-mark" aria-hidden="true"><i></i>H</span>
          <span class="brand-copy"><strong>HooviePack</strong></span>
        </div>
        <div class="step-dots" aria-label="Onboarding progress">
          <span class="is-active">1</span><i></i><span [class.is-active]="step() === 'profile'">2</span>
        </div>
      </header>

      @if (step() === 'family') {
        <section class="onboarding-card">
          <div class="onboarding-intro">
            <span class="welcome-paw" aria-hidden="true">●</span>
            <p class="eyebrow">Welcome to the pack</p>
            <h1>Let’s find your family</h1>
            <p>Create a new private space or use an invite from someone you love.</p>
          </div>

          <div class="segmented-control" role="tablist" aria-label="Choose how to get started">
            <button type="button" role="tab" [attr.aria-selected]="mode() === 'create'" [class.is-active]="mode() === 'create'" (click)="mode.set('create')">Create a family</button>
            <button type="button" role="tab" [attr.aria-selected]="mode() === 'join'" [class.is-active]="mode() === 'join'" (click)="mode.set('join')">Use invite code</button>
          </div>

          @if (mode() === 'create') {
            <form [formGroup]="createForm" (ngSubmit)="createFamily()" class="stack-form" novalidate>
              <div class="field">
                <label for="family-name">Family name</label>
                <input id="family-name" formControlName="name" maxlength="80" autocomplete="organization" placeholder="The Star Family">
                @if (showError(createForm.controls.name)) { <p class="field-error">Give your pack a name (2–80 characters).</p> }
              </div>
              <div class="field">
                <label for="family-description">A little about your pack <span>Optional</span></label>
                <textarea id="family-description" formControlName="description" maxlength="300" rows="3" placeholder="The people, pups, and everyday moments we love."></textarea>
                <small>{{ createForm.controls.description.value.length }}/300</small>
              </div>
              @if (error()) { <div class="alert alert--error" role="alert"><span>!</span><p>{{ error() }}</p></div> }
              <button class="button button--large button--full" type="submit" [disabled]="submitting()">
                {{ submitting() ? 'Making your pack…' : 'Create my family' }}
              </button>
            </form>
          } @else {
            <form [formGroup]="joinForm" (ngSubmit)="joinFamily()" class="stack-form" novalidate>
              <div class="field">
                <label for="invite-code">Invite code</label>
                <input id="invite-code" formControlName="code" maxlength="64" autocomplete="one-time-code" placeholder="e.g. HERMES-7P4K" class="code-input">
                <small>Paste the code or the full invitation link you received.</small>
                @if (showError(joinForm.controls.code)) { <p class="field-error">Enter a valid invite code.</p> }
              </div>
              @if (error()) { <div class="alert alert--error" role="alert"><span>!</span><p>{{ error() }}</p></div> }
              <button class="button button--large button--full" type="submit" [disabled]="submitting()">
                {{ submitting() ? 'Joining the pack…' : 'Join family' }}
              </button>
            </form>
          }
          <p class="privacy-note"><span aria-hidden="true">◇</span> Family spaces are private and invitation-only.</p>
        </section>
      } @else {
        <section class="onboarding-card onboarding-card--profile">
          <div class="onboarding-intro">
            <span class="welcome-paw welcome-paw--sage" aria-hidden="true">✓</span>
            <p class="eyebrow">You’re in {{ pendingFamily()?.name }}</p>
            <h1>Make yourself at home</h1>
            <p>Add a name and a little context so everyone knows it’s you.</p>
          </div>
          <form [formGroup]="profileForm" (ngSubmit)="saveProfile()" class="stack-form" novalidate>
            <hp-image-uploader [maxFiles]="1" [disabled]="submitting()" (filesChange)="profilePhoto.set($event[0])" />
            <div class="field">
              <label for="display-name">Display name</label>
              <input id="display-name" formControlName="displayName" maxlength="80" autocomplete="name" placeholder="How your family knows you">
              @if (showError(profileForm.controls.displayName)) { <p class="field-error">Enter at least 2 characters.</p> }
            </div>
            <div class="field">
              <label for="profile-bio">Short bio <span>Optional</span></label>
              <textarea id="profile-bio" formControlName="bio" maxlength="280" rows="3" placeholder="Favorite family role, current obsession, or dog treat supplier…"></textarea>
            </div>
            @if (error()) { <div class="alert alert--error" role="alert"><span>!</span><p>{{ error() }}</p></div> }
            <button class="button button--large button--full" type="submit" [disabled]="submitting()">{{ submitting() ? 'Saving…' : 'Finish setup' }}</button>
            <button class="button button--text button--full" type="button" (click)="finish()" [disabled]="submitting()">I’ll do this later</button>
          </form>
        </section>
      }
    </main>
  `,
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
