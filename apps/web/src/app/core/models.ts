export type MembershipRole = 'Owner' | 'Admin' | 'Member';
export type ReactionType = 'paw' | 'heart' | 'bone';

export interface UserProfile {
  id: string;
  authProviderUserId?: string;
  email: string;
  displayName: string;
  avatarUrl?: string | null;
  bio?: string | null;
  createdAt?: string;
  lastSeenAt?: string;
}

export interface FamilySummary {
  id: string;
  name: string;
  slug?: string;
  description?: string | null;
  role: MembershipRole;
  memberCount?: number;
  dogCount?: number;
  createdAt?: string;
}

export interface FamilyMember {
  id: string;
  userId: string;
  familyId?: string;
  displayName: string;
  email?: string;
  avatarUrl?: string | null;
  bio?: string | null;
  role: MembershipRole;
  joinedAt?: string;
}

export interface FamilyInvite {
  id: string;
  code: string;
  inviteUrl?: string;
  expiresAt: string;
  createdAt?: string;
}

export interface DogProfile {
  id: string;
  familyId: string;
  name: string;
  photoUrl?: string | null;
  breed?: string | null;
  birthday?: string | null;
  approximateAge?: string | null;
  bio?: string | null;
  favoriteThing?: string | null;
  ownerMemberId?: string | null;
  ownerDisplayName?: string | null;
  createdAt?: string;
  canEdit?: boolean;
}

export interface PostPhoto {
  id: string;
  url: string;
  originalFileName?: string;
  contentType?: string;
  width?: number;
  height?: number;
  sortOrder?: number;
}

export interface Comment {
  id: string;
  postId: string;
  authorUserId: string;
  authorDisplayName: string;
  authorAvatarUrl?: string | null;
  content: string;
  createdAt: string;
  updatedAt?: string;
  canDelete?: boolean;
}

export interface ReactionSummary {
  type: ReactionType;
  count: number;
  reactedByMe: boolean;
}

export interface Post {
  id: string;
  familyId: string;
  authorUserId: string;
  authorMemberId?: string;
  authorDisplayName: string;
  authorAvatarUrl?: string | null;
  content: string;
  createdAt: string;
  updatedAt?: string;
  isEdited?: boolean;
  photos: PostPhoto[];
  comments: Comment[];
  commentCount?: number;
  reactions: ReactionSummary[];
  canEdit?: boolean;
  canDelete?: boolean;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
}

export interface CreateFamilyRequest {
  name: string;
  description?: string;
}

export interface UpdateFamilyRequest {
  name: string;
  description?: string;
}

export interface CreateCommentRequest {
  content: string;
}

export interface UpdateProfileRequest {
  displayName: string;
  bio?: string;
}

export interface ApiErrorBody {
  title?: string;
  detail?: string;
  message?: string;
  errors?: Record<string, string[]>;
}
