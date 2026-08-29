import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin, of } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { DogProfile, FamilyMember } from '../../core/models';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { ToastService } from '../../core/toast.service';
import { ImageUploaderComponent } from '../../shared/image-uploader.component';
import { UiStateComponent } from '../../shared/ui-state.component';
import { AuthImageDirective } from '../../shared/auth-image.directive';

@Component({
  selector: 'hp-dog-editor',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthImageDirective, ImageUploaderComponent, UiStateComponent],
  templateUrl: './dog-editor.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DogEditorComponent implements OnInit {
  readonly editing = signal(false);
  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly formError = signal('');
  readonly submitting = signal(false);
  readonly members = signal<FamilyMember[]>([]);
  readonly photo = signal<File | undefined>(undefined);
  readonly existingPhoto = signal<string | null>(null);
  readonly removePhoto = signal(false);
  readonly today = new Date().toISOString().slice(0, 10);
  readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(80)] }),
    breed: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(100)] }),
    birthday: new FormControl('', { nonNullable: true }),
    approximateAgeYears: new FormControl<number | null>(null, [Validators.min(0), Validators.max(40)]),
    ownerMemberId: new FormControl('', { nonNullable: true }),
    bio: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
    favoriteThing: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(120)] })
  });
  private dogId: string | null = null;

  constructor(
    private readonly families: ActiveFamilyService,
    private readonly api: ApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly config: RuntimeConfigService,
    private readonly toasts: ToastService
  ) {}

  ngOnInit(): void {
    const familyId = this.families.activeId();
    this.dogId = this.route.snapshot.paramMap.get('dogId');
    this.editing.set(!!this.dogId);
    if (!familyId) {
      this.loading.set(false);
      this.loadError.set('Choose a family before adding a dog.');
      return;
    }
    forkJoin({
      members: this.api.listMembers(familyId),
      dog: this.dogId ? this.api.getDog(familyId, this.dogId) : of(null)
    }).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: ({ members, dog }) => {
        this.members.set(members);
        if (dog) this.populate(dog);
      },
      error: (error) => this.loadError.set(apiErrorMessage(error, 'This dog profile is unavailable.'))
    });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.formError.set('Check the highlighted details and try again.');
      return;
    }
    const familyId = this.families.activeId();
    if (!familyId) return;
    const raw = this.form.getRawValue();
    const values: Record<string, string> = {
      name: raw.name.trim(), breed: raw.breed.trim(), birthday: raw.birthday,
      approximateAgeYears: raw.birthday ? '' : (raw.approximateAgeYears?.toString() || ''),
      ownerMembershipId: raw.ownerMemberId, bio: raw.bio.trim(), favoriteThing: raw.favoriteThing.trim(),
      removePhoto: this.removePhoto().toString()
    };
    this.submitting.set(true);
    this.formError.set('');
    this.api.saveDog(familyId, values, this.photo(), this.dogId ?? undefined).pipe(
      finalize(() => this.submitting.set(false))
    ).subscribe({
      next: (dog) => {
        this.toasts.success(this.editing() ? `${dog.name}’s profile is updated.` : `${dog.name} joined the pack!`);
        void this.router.navigate(['/dogs', dog.id]);
      },
      error: (error) => this.formError.set(apiErrorMessage(error, 'We could not save this dog profile.'))
    });
  }

  showError(controlName: 'name'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.dirty || control.touched);
  }

  mediaUrl(path: string): string { return this.config.mediaUrl(path) ?? ''; }
  goToDogs(): void { void this.router.navigateByUrl('/dogs'); }

  selectPhoto(file?: File): void {
    this.photo.set(file);
    if (file) this.removePhoto.set(false);
    this.formError.set('');
  }

  removeCurrentPhoto(): void {
    this.existingPhoto.set(null);
    this.removePhoto.set(true);
  }

  private populate(dog: DogProfile): void {
    this.form.patchValue({
      name: dog.name,
      breed: dog.breed || '',
      birthday: dog.birthday?.slice(0, 10) || '',
      approximateAgeYears: dog.birthday ? null : Number.parseInt(dog.approximateAge || '', 10) || null,
      ownerMemberId: dog.ownerMemberId || '',
      bio: dog.bio || '',
      favoriteThing: dog.favoriteThing || ''
    });
    this.existingPhoto.set(dog.photoUrl || null);
  }
}
