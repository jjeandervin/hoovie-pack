import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { FamilyMember } from '../../core/models';
import { AvatarComponent } from '../../shared/avatar.component';
import { UiStateComponent } from '../../shared/ui-state.component';

@Component({
  selector: 'hp-member-detail',
  standalone: true,
  imports: [RouterLink, AvatarComponent, UiStateComponent],
  template: `
    <div class="page detail-page">
      <a class="back-link" routerLink="/family"><span aria-hidden="true">←</span> Back to family</a>
      @if (loading()) {
        <hp-ui-state kind="loading" heading="Finding your family member…" />
      } @else if (error()) {
        <hp-ui-state kind="error" heading="We couldn’t find that member" [message]="error()" actionLabel="Back to family" (action)="goBack()" />
      } @else if (member()) {
        <article class="profile-hero profile-hero--member">
          <div class="profile-hero__pattern" aria-hidden="true"><span>●</span><span>●</span><span>●</span></div>
          <hp-avatar [src]="member()!.avatarUrl" [name]="member()!.displayName" [size]="112" />
          <div><span class="role-badge">{{ member()!.role }}</span><h1>{{ member()!.displayName }}</h1><p>{{ member()!.bio || 'A loved member of ' + families.activeFamily()?.name + '.' }}</p>@if (member()!.joinedAt) { <small>In the pack since {{ joinedDate(member()!.joinedAt!) }}</small> }</div>
        </article>
        <section class="detail-card">
          <p class="eyebrow">Family connection</p>
          <h2>Part of {{ families.activeFamily()?.name }}</h2>
          <p>Profiles and everything shared in HooviePack stay visible only to members of this family.</p>
        </section>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MemberDetailComponent implements OnInit {
  readonly member = signal<FamilyMember | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');

  constructor(
    readonly families: ActiveFamilyService,
    private readonly api: ApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    const memberId = this.route.snapshot.paramMap.get('memberId');
    const familyId = this.families.activeId();
    if (!memberId || !familyId) {
      this.loading.set(false);
      this.error.set('No family member was selected.');
      return;
    }
    this.api.getMember(familyId, memberId).pipe(finalize(() => this.loading.set(false))).subscribe({
      next: (member) => this.member.set(member),
      error: (error) => this.error.set(apiErrorMessage(error, 'This profile may no longer be available.'))
    });
  }

  joinedDate(value: string): string {
    return new Date(value).toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
  }

  goBack(): void {
    void this.router.navigateByUrl('/family');
  }
}
