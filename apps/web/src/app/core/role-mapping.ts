import type { MembershipRole } from './models';

export type ApiMembershipRole = MembershipRole | Lowercase<MembershipRole> | 0 | 1 | 2;

export function mapMembershipRole(role: ApiMembershipRole): MembershipRole {
  if (role === 0 || role === 'Owner' || role === 'owner') return 'Owner';
  if (role === 1 || role === 'Admin' || role === 'admin') return 'Admin';
  return 'Member';
}
