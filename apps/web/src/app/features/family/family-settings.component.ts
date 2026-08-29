import { ChangeDetectionStrategy, Component, OnInit, effect, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { ActiveFamilyService } from '../../core/active-family.service';
import { ApiService } from '../../core/api.service';
import { apiErrorMessage } from '../../core/api-error';
import { FamilyInvite, FamilyMember, MembershipRole } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { AvatarComponent } from '../../shared/avatar.component';
import { UiStateComponent } from '../../shared/ui-state.component';

@Component({
  selector: 'hp-family-settings',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AvatarComponent, UiStateComponent],
  templateUrl: './family-settings.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FamilySettingsComponent implements OnInit {
  readonly members = signal<FamilyMember[]>([]);
  readonly invite = signal<FamilyInvite | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly familySaving = signal(false);
  readonly familyError = signal('');
  readonly inviteBusy = signal(false);
  readonly inviteError = signal('');
  readonly memberBusy = signal<string | null>(null);
  readonly copied = signal(false);
  readonly inviteDays = new FormControl(7, { nonNullable: true });
  readonly familyForm = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(2), Validators.maxLength(80)] }),
    description: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(300)] })
  });
  private loadedFamilyId: string | null = null;

  constructor(
    readonly families: ActiveFamilyService,
    private readonly api: ApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly toasts: ToastService
  ) {
    effect(() => {
      const familyId = this.families.activeId();
      if (familyId && familyId !== this.loadedFamilyId) {
        this.loadedFamilyId = familyId;
        this.load();
      }
    });
  }

  ngOnInit(): void {
    if (this.route.snapshot.queryParamMap.has('invite')) {
      window.setTimeout(() => document.getElementById('invite-heading')?.scrollIntoView({ behavior: 'smooth' }), 250);
    }
  }

  load(): void {
    const familyId = this.families.activeId();
    if (!familyId || !this.families.canManage()) {
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.loadError.set('');
    forkJoin({ family: this.api.getFamily(familyId), members: this.api.listMembers(familyId) }).pipe(
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: ({ family, members }) => {
        this.familyForm.setValue({ name: family.name, description: family.description || '' });
        this.members.set(members);
      },
      error: (error) => this.loadError.set(apiErrorMessage(error, 'We could not load family settings.'))
    });
  }

  saveFamily(): void {
    const familyId = this.families.activeId();
    if (!familyId || this.familyForm.invalid) return;
    this.familySaving.set(true);
    this.familyError.set('');
    this.api.updateFamily(familyId, this.familyForm.getRawValue()).pipe(
      finalize(() => this.familySaving.set(false))
    ).subscribe({
      next: (family) => {
        this.families.upsert(family);
        this.toasts.success('Family details updated.');
      },
      error: (error) => this.familyError.set(apiErrorMessage(error, 'We could not save those details.'))
    });
  }

  generateInvite(): void {
    const familyId = this.families.activeId();
    if (!familyId) return;
    this.inviteBusy.set(true);
    this.inviteError.set('');
    this.copied.set(false);
    this.api.createInvite(familyId, this.inviteDays.value).pipe(
      finalize(() => this.inviteBusy.set(false))
    ).subscribe({
      next: (invite) => this.invite.set(invite),
      error: (error) => this.inviteError.set(apiErrorMessage(error, 'We could not create an invite.'))
    });
  }

  inviteUrl(): string {
    const invite = this.invite();
    return invite?.inviteUrl || `${window.location.origin}/onboarding?code=${encodeURIComponent(invite?.code || '')}`;
  }

  async copyInvite(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.inviteUrl());
      this.copied.set(true);
      window.setTimeout(() => this.copied.set(false), 2200);
    } catch {
      this.inviteError.set('Copying was blocked by your browser. Select the link above and copy it manually.');
    }
  }

  changeRole(member: FamilyMember, event: Event): void {
    const familyId = this.families.activeId();
    const role = (event.target as HTMLSelectElement).value as MembershipRole;
    if (!familyId || !['Admin', 'Member'].includes(role)) return;
    const previousRole = member.role;
    this.memberBusy.set(member.id);
    this.members.update((members) => members.map((item) => item.id === member.id ? { ...item, role } : item));
    this.api.updateMemberRole(familyId, member.id, role).pipe(finalize(() => this.memberBusy.set(null))).subscribe({
      error: (error) => {
        this.members.update((members) => members.map((item) => item.id === member.id ? { ...item, role: previousRole } : item));
        this.toasts.error(apiErrorMessage(error, 'That role could not be changed.'));
      }
    });
  }

  removeMember(member: FamilyMember): void {
    const familyId = this.families.activeId();
    if (!familyId || !window.confirm(`Remove ${member.displayName} from this family?`)) return;
    this.memberBusy.set(member.id);
    this.api.removeMember(familyId, member.id).pipe(finalize(() => this.memberBusy.set(null))).subscribe({
      next: () => {
        this.members.update((members) => members.filter((item) => item.id !== member.id));
        this.toasts.success(`${member.displayName} was removed from the family.`);
      },
      error: (error) => this.toasts.error(apiErrorMessage(error, 'That family member could not be removed.'))
    });
  }

  inviteDate(value: string): string {
    return new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  }

  isOwner(): boolean {
    return this.families.activeFamily()?.role === 'Owner';
  }

  goBack(): void {
    void this.router.navigateByUrl('/family');
  }
}
