import { ChangeDetectionStrategy, Component, HostListener, computed, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { Comment, Post, PostPhoto, ReactionSummary, ReactionType } from '../../core/models';
import { RuntimeConfigService } from '../../core/runtime-config.service';
import { ToastService } from '../../core/toast.service';
import { AvatarComponent } from '../../shared/avatar.component';
import { AuthImageDirective } from '../../shared/auth-image.directive';

const REACTIONS: ReadonlyArray<{ type: ReactionType; label: string; icon: string }> = [
  { type: 'paw', label: 'Paw', icon: '●' },
  { type: 'heart', label: 'Love', icon: '♥' },
  { type: 'bone', label: 'Treat', icon: '⌁' }
];

@Component({
  selector: 'hp-post-card',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AvatarComponent, AuthImageDirective],
  template: `
    <article class="post-card" [attr.aria-labelledby]="'post-author-' + post().id">
      <header class="post-card__header">
        <a class="post-author" [routerLink]="['/members', post().authorUserId]">
          <hp-avatar [src]="post().authorAvatarUrl" [name]="post().authorDisplayName" [size]="46" />
          <span>
            <strong [id]="'post-author-' + post().id">{{ post().authorDisplayName }}</strong>
            <small><time [attr.datetime]="post().createdAt" [title]="fullDate(post().createdAt)">{{ relativeTime(post().createdAt) }}</time>@if (post().isEdited) { · edited } · <span aria-label="Private family post">◇</span></small>
          </span>
        </a>
        @if (post().canEdit || post().canDelete) {
          <details class="action-menu">
            <summary class="icon-button" aria-label="Post options">•••</summary>
            <div class="action-menu__panel">
              @if (post().canEdit) { <a [routerLink]="['/posts', post().id, 'edit']">Edit post</a> }
              @if (post().canDelete) { <button type="button" class="danger-text" (click)="removePost()">Delete post</button> }
            </div>
          </details>
        }
      </header>

      @if (post().content) { <p class="post-content">{{ post().content }}</p> }

      @if (post().photos.length) {
        <div class="post-photos" [class]="'post-photos post-photos--' + photoClass()" aria-label="Post photos">
          @for (photo of post().photos; track photo.id; let index = $index) {
            <button type="button" class="post-photo" (click)="openPhoto(photo)" [attr.aria-label]="'Open photo ' + (index + 1) + ' of ' + post().photos.length">
              <img [hpAuthImage]="photo.url" [alt]="photo.originalFileName || 'Family post photo'" loading="lazy">
              @if (index === 3 && post().photos.length > 4) { <span>+{{ post().photos.length - 4 }}</span> }
            </button>
          }
        </div>
      }

      @if (totalReactions() || commentCount()) {
        <div class="post-card__summary">
          <span>@if (totalReactions()) { <b aria-hidden="true">♥</b> {{ totalReactions() }} {{ totalReactions() === 1 ? 'reaction' : 'reactions' }} }</span>
          <button type="button" (click)="commentsOpen.set(!commentsOpen())">{{ commentCount() }} {{ commentCount() === 1 ? 'comment' : 'comments' }}</button>
        </div>
      }

      <div class="reaction-bar" aria-label="React to this post">
        @for (reaction of reactionOptions; track reaction.type) {
          <button
            type="button"
            [class.is-active]="reactionState(reaction.type).reactedByMe"
            [disabled]="reactionBusy() === reaction.type"
            (click)="toggleReaction(reaction.type)"
            [attr.aria-pressed]="reactionState(reaction.type).reactedByMe"
            [attr.aria-label]="reaction.label + ' reaction, ' + reactionState(reaction.type).count"
          >
            <span [class]="'reaction-icon reaction-icon--' + reaction.type" aria-hidden="true">{{ reaction.icon }}</span>
            <span>{{ reaction.label }}</span>
            @if (reactionState(reaction.type).count) { <small>{{ reactionState(reaction.type).count }}</small> }
          </button>
        }
        <button type="button" class="comment-toggle" [class.is-active]="commentsOpen()" (click)="commentsOpen.set(!commentsOpen())" [attr.aria-expanded]="commentsOpen()">
          <span aria-hidden="true">□</span><span>Comment</span>
        </button>
      </div>

      @if (commentsOpen()) {
        <section class="comments" [attr.aria-label]="'Comments on ' + post().authorDisplayName + '’s post'">
          @if (post().comments.length) {
            <ul class="comment-list">
              @for (comment of post().comments; track comment.id) {
                <li>
                  <hp-avatar [src]="comment.authorAvatarUrl" [name]="comment.authorDisplayName" [size]="34" />
                  <div class="comment-bubble">
                    <span><strong>{{ comment.authorDisplayName }}</strong><time [attr.datetime]="comment.createdAt">{{ relativeTime(comment.createdAt) }}</time></span>
                    <p>{{ comment.content }}</p>
                  </div>
                  @if (comment.canDelete) {
                    <button type="button" class="icon-button comment-delete" (click)="removeComment(comment)" aria-label="Delete comment">×</button>
                  }
                </li>
              }
            </ul>
          } @else {
            <p class="comments-empty">No comments yet. Be the first to say something kind.</p>
          }

          <form class="comment-form" (ngSubmit)="submitComment()">
            <label class="sr-only" [for]="'comment-' + post().id">Write a comment</label>
            <input [id]="'comment-' + post().id" [formControl]="commentControl" maxlength="500" placeholder="Write a comment…" autocomplete="off">
            <button type="submit" class="icon-button comment-send" [disabled]="commentControl.invalid || commentBusy()" aria-label="Post comment">➜</button>
          </form>
          @if (commentError()) { <p class="field-error" role="alert">{{ commentError() }}</p> }
        </section>
      }
    </article>

    @if (selectedPhoto()) {
      <div class="lightbox" role="dialog" aria-modal="true" aria-label="Photo viewer" (click)="closePhoto()">
        <button type="button" class="lightbox__close" (click)="closePhoto()" aria-label="Close photo viewer">×</button>
        <img [hpAuthImage]="selectedPhoto()!.url" [alt]="selectedPhoto()!.originalFileName || 'Family post photo'" (click)="$event.stopPropagation()">
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PostCardComponent {
  readonly post = input.required<Post>();
  readonly changed = output<Post>();
  readonly deleted = output<string>();
  readonly reactionOptions = REACTIONS;
  readonly commentsOpen = signal(false);
  readonly reactionBusy = signal<ReactionType | null>(null);
  readonly commentBusy = signal(false);
  readonly commentError = signal('');
  readonly selectedPhoto = signal<PostPhoto | null>(null);
  readonly commentControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(500)]
  });
  readonly totalReactions = computed(() => (this.post().reactions ?? []).reduce((sum, item) => sum + item.count, 0));
  readonly commentCount = computed(() => this.post().commentCount ?? this.post().comments?.length ?? 0);
  readonly photoClass = computed(() => Math.min(this.post().photos?.length ?? 0, 4));

  constructor(
    private readonly api: ApiService,
    private readonly config: RuntimeConfigService,
    private readonly toasts: ToastService
  ) {}

  reactionState(type: ReactionType): ReactionSummary {
    return this.post().reactions?.find((reaction) => reaction.type.toLowerCase() === type) ?? { type, count: 0, reactedByMe: false };
  }

  toggleReaction(type: ReactionType): void {
    if (this.reactionBusy()) return;
    const before = this.post();
    const current = this.reactionState(type);
    const reactions = this.reactionOptions.map(({ type: optionType }) => {
      const item = this.reactionState(optionType);
      return optionType === type
        ? { ...item, reactedByMe: !item.reactedByMe, count: Math.max(0, item.count + (item.reactedByMe ? -1 : 1)) }
        : item;
    });
    this.changed.emit({ ...before, reactions });
    this.reactionBusy.set(type);
    const request = current.reactedByMe ? this.api.removeReaction(before.id, type) : this.api.addReaction(before.id, type);
    request.pipe(finalize(() => this.reactionBusy.set(null))).subscribe({
      next: (serverReactions) => {
        if (Array.isArray(serverReactions) && serverReactions.length) this.changed.emit({ ...this.post(), reactions: serverReactions });
      },
      error: (error) => {
        this.changed.emit(before);
        this.toasts.error(apiErrorMessage(error, 'That reaction did not stick.'));
      }
    });
  }

  submitComment(): void {
    if (this.commentControl.invalid || this.commentBusy()) {
      this.commentControl.markAsTouched();
      return;
    }
    this.commentBusy.set(true);
    this.commentError.set('');
    this.api.addComment(this.post().id, { content: this.commentControl.value.trim() }).pipe(
      finalize(() => this.commentBusy.set(false))
    ).subscribe({
      next: (comment) => {
        const comments = [...(this.post().comments ?? []), comment];
        this.changed.emit({ ...this.post(), comments, commentCount: this.commentCount() + 1 });
        this.commentControl.reset();
      },
      error: (error) => this.commentError.set(apiErrorMessage(error, 'Your comment could not be posted.'))
    });
  }

  removeComment(comment: Comment): void {
    if (!window.confirm('Delete this comment?')) return;
    this.api.deleteComment(this.post().id, comment.id).subscribe({
      next: () => {
        const comments = (this.post().comments ?? []).filter((item) => item.id !== comment.id);
        this.changed.emit({ ...this.post(), comments, commentCount: Math.max(0, this.commentCount() - 1) });
      },
      error: (error) => this.toasts.error(apiErrorMessage(error, 'The comment could not be deleted.'))
    });
  }

  removePost(): void {
    if (!window.confirm('Delete this post and its photos? This cannot be undone.')) return;
    this.api.deletePost(this.post().id).subscribe({
      next: () => {
        this.deleted.emit(this.post().id);
        this.toasts.success('Post deleted.');
      },
      error: (error) => this.toasts.error(apiErrorMessage(error, 'The post could not be deleted.'))
    });
  }

  mediaUrl(path: string): string {
    return this.config.mediaUrl(path) ?? '';
  }

  relativeTime(value: string): string {
    const date = new Date(value);
    const seconds = Math.round((date.getTime() - Date.now()) / 1000);
    if (!Number.isFinite(seconds)) return '';
    const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
    if (Math.abs(seconds) < 60) return formatter.format(seconds, 'second');
    const minutes = Math.round(seconds / 60);
    if (Math.abs(minutes) < 60) return formatter.format(minutes, 'minute');
    const hours = Math.round(minutes / 60);
    if (Math.abs(hours) < 24) return formatter.format(hours, 'hour');
    const days = Math.round(hours / 24);
    if (Math.abs(days) < 7) return formatter.format(days, 'day');
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: date.getFullYear() !== new Date().getFullYear() ? 'numeric' : undefined });
  }

  fullDate(value: string): string {
    return new Date(value).toLocaleString();
  }

  openPhoto(photo: PostPhoto): void {
    this.selectedPhoto.set(photo);
    document.body.classList.add('modal-open');
  }

  closePhoto(): void {
    this.selectedPhoto.set(null);
    document.body.classList.remove('modal-open');
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.selectedPhoto()) this.closePhoto();
  }
}
