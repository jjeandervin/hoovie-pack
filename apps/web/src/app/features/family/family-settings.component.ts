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
  template: `
    <div class="page settings-page">
      <a class="back-link" routerLink="/family"><span aria-hidden="true">←</span> Back to family</a>
      <header class="page-heading"><div><p class="eyebrow">Owner & admin tools</p><h1>Family settings</h1><p>Keep your family details, invitations, and member access up to date.</p></div></header>

      @if (!families.canManage()) {
        <hp-ui-state kind="error" heading="Admin access needed" message="Only family owners and admins can change these settings." actionLabel="Back to family" (action)="goBack()" />
      } @else if (loading()) {
        <hp-ui-state kind="loading" heading="Opening settings…" [compact]="true" />
      } @else if (loadError()) {
        <hp-ui-state kind="error" heading="Settings are unavailable" [message]="loadError()" actionLabel="Try again" (action)="load()" />
      } @else {
        <section class="settings-card" aria-labelledby="details-heading">
          <div class="settings-card__heading"><span aria-hidden="true">⌂</span><div><h2 id="details-heading">Family details</h2><p>What everyone sees at the top of your shared space.</p></div></div>
          <form [formGroup]="familyForm" (ngSubmit)="saveFamily()" class="stack-form settings-form">
            <div class="field"><label for="settings-name">Family name</label><input id="settings-name" formControlName="name" maxlength="80"></div>
            <div class="field"><label for="settings-description">Description <span>Optional</span></label><textarea id="settings-description" formControlName="description" rows="3" maxlength="300"></textarea><small>{{ familyForm.controls.description.value.length }}/300</small></div>
            @if (familyError()) { <p class="field-error" role="alert">{{ familyError() }}</p> }
            <div class="form-actions"><button type="submit" class="button" [disabled]="familySaving() || familyForm.invalid">{{ familySaving() ? 'Saving…' : 'Save details' }}</button></div>
          </form>
        </section>

        <section class="settings-card settings-card--accent" aria-labelledby="invite-heading">
          <div class="settings-card__heading"><span aria-hidden="true">＋</span><div><h2 id="invite-heading">Invite family</h2><p>Create a private, expiring link for someone you trust.</p></div></div>
          <div class="invite-builder">
            <div class="field"><label for="invite-expiry">Link expires</label><select id="invite-expiry" [formControl]="inviteDays"><option [ngValue]="1">In 1 day</option><option [ngValue]="7">In 7 days</option><option [ngValue]="30">In 30 days</option></select></div>
            <button type="button" class="button" (click)="generateInvite()" [disabled]="inviteBusy()">{{ inviteBusy() ? 'Creating…' : 'Create invite link' }}</button>
          </div>
          @if (inviteError()) { <p class="field-error" role="alert">{{ inviteError() }}</p> }
          @if (invite()) {
            <div class="invite-result" aria-live="polite">
              <div><small>Private invitation</small><code>{{ inviteUrl() }}</code><span>Expires {{ inviteDate(invite()!.expiresAt) }}</span></div>
              <button type="button" class="button button--secondary" (click)="copyInvite()">{{ copied() ? 'Copied!' : 'Copy link' }}</button>
            </div>
          }
        </section>

        <section class="settings-card" aria-labelledby="manage-members-heading">
          <div class="settings-card__heading"><span aria-hidden="true">♧</span><div><h2 id="manage-members-heading">Members</h2><p>Choose who can invite others and manage the family.</p></div></div>
          <div class="settings-member-list">
            @for (member of members(); track member.id) {
              <div class="settings-member">
                <hp-avatar [src]="member.avatarUrl" [name]="member.displayName" [size]="46" />
                <div><strong>{{ member.displayName }}</strong><small>{{ member.email }}</small></div>
                @if (member.role === 'Owner') {
                  <span class="role-chip role-chip--owner">Owner</span>
                } @else if (isOwner()) {
                  <label class="sr-only" [for]="'role-' + member.id">Role for {{ member.displayName }}</label>
                  <select [id]="'role-' + member.id" [value]="member.role" (change)="changeRole(member, $event)" [disabled]="memberBusy() === member.id"><option value="Member">Member</option><option value="Admin">Admin</option></select>
                  <button type="button" class="icon-button danger-text" (click)="removeMember(member)" [disabled]="memberBusy() === member.id" [attr.aria-label]="'Remove ' + member.displayName">×</button>
                } @else {
                  <span class="role-chip">{{ member.role }}</span>
                  @if (member.role === 'Member') {
                    <button type="button" class="icon-button danger-text" (click)="removeMember(member)" [disabled]="memberBusy() === member.id" [attr.aria-label]="'Remove ' + member.displayName">×</button>
                  }
                }
              </div>
            }
          </div>
        </section>
      }
    </div>
  `,
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
