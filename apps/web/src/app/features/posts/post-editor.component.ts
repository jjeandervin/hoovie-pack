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
  template: `
    <div class="page editor-page">
      <header class="editor-header">
        <a routerLink="/feed" class="icon-button" aria-label="Close post editor">×</a>
        <div><p class="eyebrow">Family feed</p><h1>{{ editing() ? 'Edit post' : 'Share an update' }}</h1></div>
        <span></span>
      </header>

      @if (loading()) {
        <hp-ui-state kind="loading" heading="Fetching your post…" [compact]="true" />
      } @else if (loadError()) {
        <hp-ui-state kind="error" heading="We couldn’t open that post" [message]="loadError()" actionLabel="Back to feed" (action)="goToFeed()" />
      } @else {
        <form [formGroup]="form" (ngSubmit)="save()" class="composer-card" novalidate>
          <div class="composer-audience">
            <hp-avatar [src]="user.profile()?.avatarUrl" [name]="user.profile()?.displayName || 'You'" [size]="48" />
            <div><strong>{{ user.profile()?.displayName || 'You' }}</strong><span><i aria-hidden="true">◇</i> {{ families.activeFamily()?.name }} · Private</span></div>
          </div>

          <div class="field field--bare">
            <label class="sr-only" for="post-content">Post text</label>
            <textarea
              id="post-content"
              formControlName="content"
              maxlength="2000"
              rows="7"
              autofocus
              placeholder="What’s new in the pack?"
              (input)="formError.set('')"
            ></textarea>
            <span class="character-count" [class.character-count--near]="form.controls.content.value.length > 1800">{{ form.controls.content.value.length }}/2,000</span>
          </div>

          @if (existingPhotos().length) {
            <div class="existing-photos" aria-label="Photos already on this post">
              @for (photo of existingPhotos(); track photo.id) {
                <figure><img [hpAuthImage]="photo.url" [alt]="photo.originalFileName || 'Existing post photo'"><button type="button" (click)="removeExistingPhoto(photo)" [attr.aria-label]="'Remove ' + (photo.originalFileName || 'photo')">×</button></figure>
              }
              <p>Existing photos stay with this post.</p>
            </div>
          }

          <hp-image-uploader [maxFiles]="remainingPhotoSlots()" [disabled]="submitting() || remainingPhotoSlots() === 0" (filesChange)="selectedPhotos.set($event); formError.set('')" />

          <div class="composer-tip"><span aria-hidden="true">●</span><p><strong>Pack tip</strong> Keep everyone smiling—share moments your family would be happy to see around the dinner table.</p></div>
          @if (formError()) { <div class="alert alert--error" role="alert"><span>!</span><p>{{ formError() }}</p></div> }

          <footer class="composer-actions">
            <a routerLink="/feed" class="button button--text">Cancel</a>
            <button type="submit" class="button button--large" [disabled]="submitting()">
              {{ submitting() ? 'Sharing…' : editing() ? 'Save changes' : 'Share with the pack' }}
            </button>
          </footer>
        </form>
      }
    </div>
  `,
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
