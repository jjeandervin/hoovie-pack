import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { CurrentUserService } from '../../core/current-user.service';
import { PostPhoto } from '../../core/models';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { ToastService } from '../../core/toast.service';
import { AvatarComponent } from '../../shared/avatar.component';
import { AuthImageDirective } from '../../shared/auth-image.directive';
import { ImageUploaderComponent } from '../../shared/image-uploader.component';
import { UiStateComponent } from '../../shared/ui-state.component';

@Component({
  selector: 'hp-post-editor',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AvatarComponent, AuthImageDirective, ImageUploaderComponent, UiStateComponent],
  templateUrl: './post-editor.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PostEditorComponent implements OnInit {
  readonly editing = signal(false);
  readonly loading = signal(false);
  readonly submitting = signal(false);
  readonly loadError = signal('');
  readonly formError = signal('');
  readonly selectedPhotos = signal<File[]>([]);
  readonly existingPhotos = signal<PostPhoto[]>([]);
  readonly removedPhotoIds = signal<string[]>([]);
  readonly form = new FormGroup({
    content: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] })
  });
  private postId: string | null = null;

  constructor(
    readonly families: ActiveFamilyService,
    readonly user: CurrentUserService,
    private readonly api: ApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly config: RuntimeConfigService,
    private readonly toasts: ToastService
  ) {}

  ngOnInit(): void {
    this.postId = this.route.snapshot.paramMap.get('postId');
    this.editing.set(!!this.postId);
    if (this.postId) this.loadPost(this.postId);
  }

  save(): void {
    const content = this.form.controls.content.value.trim();
    if (!content && !this.selectedPhotos().length && !this.existingPhotos().length) {
      this.formError.set('Add a few words or at least one photo before sharing.');
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.formError.set('Posts can be up to 2,000 characters.');
      return;
    }
    const familyId = this.families.activeId();
    if (!familyId) {
      this.formError.set('Choose a family before sharing.');
      return;
    }

    this.submitting.set(true);
    this.formError.set('');
    this.api.savePost(familyId, content, this.selectedPhotos(), this.postId ?? undefined, this.removedPhotoIds()).pipe(
      finalize(() => this.submitting.set(false))
    ).subscribe({
      next: () => {
        this.toasts.success(this.editing() ? 'Post updated.' : 'Shared with your pack.');
        this.goToFeed();
      },
      error: (error) => this.formError.set(apiErrorMessage(error, 'We could not save that post.'))
    });
  }

  remainingPhotoSlots(): number {
    return Math.max(0, 4 - this.existingPhotos().length);
  }

  mediaUrl(path: string): string {
    return this.config.mediaUrl(path) ?? '';
  }

  removeExistingPhoto(photo: PostPhoto): void {
    this.existingPhotos.update((photos) => photos.filter((item) => item.id !== photo.id));
    this.removedPhotoIds.update((ids) => [...ids, photo.id]);
    this.formError.set('');
  }

  goToFeed(): void {
    void this.router.navigateByUrl('/feed');
  }

  private loadPost(postId: string): void {
    this.loading.set(true);
    this.api.getPost(postId).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (post) => {
        this.form.controls.content.setValue(post.content || '');
        this.existingPhotos.set(post.photos ?? []);
      },
      error: (error) => this.loadError.set(apiErrorMessage(error, 'That post may no longer be available.'))
    });
  }
}
