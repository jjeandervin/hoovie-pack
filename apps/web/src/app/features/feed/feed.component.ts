import { ChangeDetectionStrategy, Component, OnInit, effect, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { CurrentUserService } from '../../core/current-user.service';
import { Post } from '../../core/models';
import { AvatarComponent } from '../../shared/avatar.component';
import { UiStateComponent } from '../../shared/ui-state.component';
import { PostCardComponent } from './post-card.component';

@Component({
  selector: 'hp-feed',
  standalone: true,
  imports: [RouterLink, AvatarComponent, UiStateComponent, PostCardComponent],
  template: `
    <div class="page feed-page">
      <header class="page-heading feed-heading">
        <div>
          <p class="eyebrow">{{ greeting() }}, {{ firstName() }}</p>
          <h1>What’s new in the pack?</h1>
          <p>{{ families.activeFamily()?.description || 'The little moments are the big ones around here.' }}</p>
        </div>
        <button type="button" class="icon-button refresh-button" (click)="loadPosts(true)" [disabled]="refreshing()" aria-label="Refresh family feed">↻</button>
      </header>

      <a class="quick-composer" routerLink="/posts/new">
        <hp-avatar [src]="user.profile()?.avatarUrl" [name]="user.profile()?.displayName || 'You'" [size]="44" />
        <span>Share an update with your family…</span>
        <b aria-hidden="true">▧</b>
      </a>

      @if (loading()) {
        <section class="feed-skeletons" aria-label="Loading family posts" aria-busy="true">
          @for (item of [1, 2, 3]; track item) {
            <div class="post-skeleton"><div><i></i><span></span></div><p></p><p></p><figure></figure></div>
          }
        </section>
      } @else if (error()) {
        <hp-ui-state kind="error" heading="The feed wandered off" [message]="error()" actionLabel="Try again" (action)="loadPosts(true)" />
      } @else if (!posts().length) {
        <hp-ui-state kind="empty" icon="●" heading="Quiet pack, for now" message="Share the first update, photo, or tail-wagging moment with your family." actionLabel="Create the first post" (action)="openComposer()" />
      } @else {
        <section class="feed-list" aria-label="Family posts" [attr.aria-busy]="refreshing()">
          @for (post of posts(); track post.id) {
            <hp-post-card [post]="post" (changed)="updatePost($event)" (deleted)="removePost($event)" />
          }
        </section>
        @if (hasMore()) {
          <button type="button" class="button button--secondary load-more" (click)="loadMore()" [disabled]="loadingMore()">
            {{ loadingMore() ? 'Following the trail…' : 'Show older posts' }}
          </button>
        } @else if (posts().length > 2) {
          <p class="feed-end"><span aria-hidden="true">●</span> You’re all caught up with the pack.</p>
        }
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FeedComponent implements OnInit {
  readonly posts = signal<Post[]>([]);
  readonly loading = signal(true);
  readonly refreshing = signal(false);
  readonly loadingMore = signal(false);
  readonly error = signal('');
  readonly hasMore = signal(false);
  readonly greeting = signal(this.getGreeting());
  private page = 1;
  private loadedFamilyId: string | null = null;

  constructor(
    readonly families: ActiveFamilyService,
    readonly user: CurrentUserService,
    private readonly api: ApiService,
    private readonly router: Router
  ) {
    effect(() => {
      const familyId = this.families.activeId();
      if (familyId && familyId !== this.loadedFamilyId) {
        this.loadedFamilyId = familyId;
        this.loadPosts(true);
      }
    });
  }

  ngOnInit(): void {
    if (this.families.activeId() && !this.loadedFamilyId) this.loadPosts(true);
  }

  firstName(): string {
    return (this.user.profile()?.displayName || 'friend').trim().split(/\s+/)[0];
  }

  loadPosts(reset = false): void {
    const familyId = this.families.activeId();
    if (!familyId) return;
    if (reset) {
      this.page = 1;
      this.error.set('');
      if (this.posts().length) this.refreshing.set(true);
      else this.loading.set(true);
    }
    this.api.listPosts(familyId, this.page, 10).pipe(
      finalize(() => {
        this.loading.set(false);
        this.refreshing.set(false);
        this.loadingMore.set(false);
      })
    ).subscribe({
      next: (result) => {
        this.posts.set(reset ? result.items : [...this.posts(), ...result.items]);
        this.hasMore.set(result.hasMore);
      },
      error: (error) => this.error.set(apiErrorMessage(error, 'We could not load the family feed.'))
    });
  }

  loadMore(): void {
    if (this.loadingMore() || !this.hasMore()) return;
    this.page += 1;
    this.loadingMore.set(true);
    this.loadPosts(false);
  }

  updatePost(updated: Post): void {
    this.posts.update((posts) => posts.map((post) => (post.id === updated.id ? updated : post)));
  }

  removePost(postId: string): void {
    this.posts.update((posts) => posts.filter((post) => post.id !== postId));
  }

  openComposer(): void {
    void this.router.navigateByUrl('/posts/new');
  }

  private getGreeting(): string {
    const hour = new Date().getHours();
    if (hour < 12) return 'Good morning';
    if (hour < 18) return 'Good afternoon';
    return 'Good evening';
  }
}
