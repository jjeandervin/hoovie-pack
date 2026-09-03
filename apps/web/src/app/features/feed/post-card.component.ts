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

const REACTIONS: ReadonlyArray<{ type: ReactionType; label: string; iconUrl: string }> = [
  { type: 'paw', label: 'Paw', iconUrl: '/assets/paw.png' },
  { type: 'heart', label: 'Love', iconUrl: '/assets/heart.png' },
  { type: 'bone', label: 'Treat', iconUrl: '/assets/treat.png' }
];

@Component({
  selector: 'hp-post-card',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AvatarComponent, AuthImageDirective],
  templateUrl: './post-card.component.html',
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

  submitComment(event: Event): void {
    event.preventDefault();

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
