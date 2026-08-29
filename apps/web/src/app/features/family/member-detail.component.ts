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
  templateUrl: './member-detail.component.html',
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
