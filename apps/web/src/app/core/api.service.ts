import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, forkJoin, from, map, of, switchMap } from 'rxjs';
import {
  Comment,
  CreateCommentRequest,
  CreateFamilyRequest,
  DogProfile,
  FamilyInvite,
  FamilyMember,
  FamilySummary,
  FileReference,
  FileUploadPurpose,
  FileUploadRequest,
  FileUploadResponse,
  MembershipRole,
  PagedResult,
  Post,
  ReactionSummary,
  ReactionType,
  UpdateFamilyRequest,
  UserProfile
} from './models';
import { uploadToPresignedUrl } from './presigned-upload';
import { RuntimeConfigService } from './runtime-config.service';

@Injectable({ providedIn: 'root' })
export class ApiService {
  constructor(
    private readonly http: HttpClient,
    private readonly config: RuntimeConfigService
  ) {}

  private get baseUrl(): string {
    return this.config.apiBaseUrl;
  }

  getMe(): Observable<UserProfile> {
    return this.http.get<UserProfile>(`${this.baseUrl}/me`);
  }

  updateMe(displayName: string, bio: string, avatar?: File): Observable<UserProfile> {
    return this.http.put<UserProfile>(`${this.baseUrl}/me`, { displayName, bio }).pipe(
      switchMap((profile) => {
        if (!avatar) return of(profile);
        return this.uploadFile(avatar, 'avatar').pipe(
          switchMap((avatarFile) => this.http.post<UserProfile>(`${this.baseUrl}/me/avatar`, avatarFile))
        );
      })
    );
  }

  listFamilies(): Observable<FamilySummary[]> {
    return this.http.get<ApiFamily[]>(`${this.baseUrl}/families`).pipe(map((families) => families.map(mapFamily)));
  }

  getFamily(familyId: string): Observable<FamilySummary> {
    return this.http.get<ApiFamily>(`${this.baseUrl}/families/${familyId}`).pipe(map(mapFamily));
  }

  createFamily(request: CreateFamilyRequest): Observable<FamilySummary> {
    return this.http.post<ApiFamily>(`${this.baseUrl}/families`, request).pipe(map(mapFamily));
  }

  joinFamily(code: string): Observable<FamilySummary> {
    return this.http.post<ApiFamily>(`${this.baseUrl}/families/join`, { inviteCode: code }).pipe(map(mapFamily));
  }

  updateFamily(familyId: string, request: UpdateFamilyRequest): Observable<FamilySummary> {
    return this.http.put<ApiFamily>(`${this.baseUrl}/families/${familyId}`, request).pipe(map(mapFamily));
  }

  listMembers(familyId: string): Observable<FamilyMember[]> {
    return this.http.get<ApiMember[]>(`${this.baseUrl}/families/${familyId}/members`).pipe(
      map((members) => members.map(mapMember))
    );
  }

  getMember(familyId: string, memberId: string): Observable<FamilyMember> {
    return this.listMembers(familyId).pipe(
      map((members) => {
        const member = members.find((item) => item.id === memberId || item.userId === memberId);
        if (!member) throw new Error('Family member not found');
        return member;
      })
    );
  }

  updateMemberRole(familyId: string, memberId: string, role: MembershipRole): Observable<FamilyMember> {
    return this.http.put<ApiMember>(`${this.baseUrl}/families/${familyId}/members/${memberId}/role`, { role }).pipe(
      map(mapMember)
    );
  }

  removeMember(familyId: string, memberId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/families/${familyId}/members/${memberId}`);
  }

  createInvite(familyId: string, expiresInDays: number): Observable<FamilyInvite> {
    return this.http.post<ApiInvite>(`${this.baseUrl}/families/${familyId}/invites`, { expiresInDays }).pipe(
      map((invite) => ({
        id: invite.id,
        code: invite.inviteCode || invite.codeHint,
        expiresAt: invite.expiresAt,
        createdAt: invite.createdAt
      }))
    );
  }

  listDogs(familyId: string): Observable<DogProfile[]> {
    return this.http.get<ApiDog[]>(`${this.baseUrl}/families/${familyId}/dogs`).pipe(map((dogs) => dogs.map(mapDog)));
  }

  getDog(familyId: string, dogId: string): Observable<DogProfile> {
    return this.http.get<ApiDog>(`${this.baseUrl}/families/${familyId}/dogs/${dogId}`).pipe(map(mapDog));
  }

  saveDog(familyId: string, values: Record<string, string>, photo?: File, dogId?: string): Observable<DogProfile> {
    const photoFile: Observable<FileReference | null> = photo
      ? this.uploadFile(photo, 'dogPhoto', familyId)
      : of(null);
    return photoFile.pipe(
      switchMap((uploadedPhoto) => {
        const request: UpsertDogRequest = {
          name: values['name'] ?? '',
          breed: optionalValue(values['breed']),
          birthday: optionalValue(values['birthday']),
          approximateAgeYears: optionalNumber(values['approximateAgeYears']),
          bio: optionalValue(values['bio']),
          favoriteThing: optionalValue(values['favoriteThing']),
          ownerMembershipId: optionalValue(values['ownerMembershipId']),
          photoFile: uploadedPhoto,
          removePhoto: values['removePhoto'] === 'true'
        };
        return dogId
          ? this.http.put<ApiDog>(`${this.baseUrl}/families/${familyId}/dogs/${dogId}`, request).pipe(map(mapDog))
          : this.http.post<ApiDog>(`${this.baseUrl}/families/${familyId}/dogs`, request).pipe(map(mapDog));
      })
    );
  }

  deleteDog(familyId: string, dogId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/families/${familyId}/dogs/${dogId}`);
  }

  listPosts(familyId: string, page = 1, pageSize = 10): Observable<PagedResult<Post>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http
      .get<ApiPagedResult<ApiPost> | ApiPost[]>(`${this.baseUrl}/families/${familyId}/posts`, { params })
      .pipe(
        map((response) =>
          Array.isArray(response)
            ? { items: response.map(mapPost), page, pageSize, totalCount: response.length, hasMore: response.length === pageSize }
            : { items: response.items.map(mapPost), page: response.page, pageSize: response.pageSize, totalCount: response.totalCount, hasMore: response.page < response.totalPages }
        )
      );
  }

  getPost(postId: string): Observable<Post> {
    return this.http.get<ApiPost>(`${this.baseUrl}/posts/${postId}`).pipe(map(mapPost));
  }

  savePost(familyId: string, content: string, photos: File[], postId?: string, removedPhotoIds: string[] = []): Observable<Post> {
    const photoFiles = photos.length
      ? forkJoin(photos.map((photo) => this.uploadFile(photo, 'postPhoto', familyId)))
      : of<FileReference[]>([]);
    return photoFiles.pipe(
      switchMap((uploadedPhotos) => {
        const request: UpsertPostRequest = { content, photoFiles: uploadedPhotos, removedPhotoIds };
        return postId
          ? this.http.put<ApiPost>(`${this.baseUrl}/posts/${postId}`, request).pipe(map(mapPost))
          : this.http.post<ApiPost>(`${this.baseUrl}/families/${familyId}/posts`, request).pipe(map(mapPost));
      })
    );
  }

  deletePost(postId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/posts/${postId}`);
  }

  addComment(postId: string, request: CreateCommentRequest): Observable<Comment> {
    return this.http.post<ApiComment>(`${this.baseUrl}/posts/${postId}/comments`, request).pipe(map(mapComment));
  }

  deleteComment(postId: string, commentId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/posts/${postId}/comments/${commentId}`);
  }

  addReaction(postId: string, type: ReactionType): Observable<ReactionSummary[]> {
    return this.http.post<ApiToggleReaction>(`${this.baseUrl}/posts/${postId}/reactions/${type}`, {}).pipe(
      map((response) => mapReactions(response.reactions))
    );
  }

  removeReaction(postId: string, type: ReactionType): Observable<ReactionSummary[]> {
    return this.http.delete<ApiReactionSummary>(`${this.baseUrl}/posts/${postId}/reactions/${type}`).pipe(map(mapReactions));
  }

  private uploadFile(file: File, purpose: FileUploadPurpose, familyId?: string): Observable<FileReference> {
    const request: FileUploadRequest = {
      fileName: file.name,
      contentType: file.type,
      size: file.size,
      purpose,
      ...(familyId ? { familyId } : {})
    };
    return this.http.post<FileUploadResponse>(`${this.baseUrl}/media/uploads`, request).pipe(
      switchMap((upload) => from(uploadToPresignedUrl(file, upload)).pipe(
        map(() => ({ fileId: upload.fileId, uploadToken: upload.uploadToken }))
      ))
    );
  }
}

type ApiRole = MembershipRole | 0 | 1 | 2;
type ApiReactionType = ReactionType | 'Paw' | 'Heart' | 'Bone' | 0 | 1 | 2;

interface ApiUserSummary { id: string; displayName: string; avatarUrl?: string | null; bio?: string | null; }
interface ApiFamily { id: string; name: string; slug?: string; description?: string | null; role: ApiRole; memberCount?: number; createdAt?: string; }
interface ApiMember { membershipId: string; userId: string; displayName: string; avatarUrl?: string | null; bio?: string | null; role: ApiRole; joinedAt?: string; }
interface ApiInvite { id: string; codeHint: string; createdAt: string; expiresAt: string; inviteCode?: string | null; }
interface ApiDog { id: string; familyId: string; name: string; photoUrl?: string | null; breed?: string | null; birthday?: string | null; approximateAgeYears?: number | null; bio?: string | null; favoriteThing?: string | null; ownerMembershipId?: string | null; owner?: ApiUserSummary | null; createdAt?: string; canManage?: boolean; }
interface ApiPostPhoto { id: string; url: string; originalFileName?: string; contentType?: string; width?: number; height?: number; sortOrder?: number; }
interface ApiComment { id: string; postId: string; author: ApiUserSummary; content: string; createdAt: string; updatedAt?: string; canDelete?: boolean; }
interface ApiReactionSummary { counts: Record<string, number>; myReactions: ApiReactionType[]; }
interface ApiToggleReaction { added: boolean; reactions: ApiReactionSummary; }
interface ApiPost { id: string; familyId: string; author: ApiUserSummary; content: string; createdAt: string; updatedAt?: string; isEdited?: boolean; canEdit?: boolean; canDelete?: boolean; photos?: ApiPostPhoto[]; comments?: ApiComment[]; commentCount?: number; reactions?: ApiReactionSummary; }
interface ApiPagedResult<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }
interface UpsertDogRequest { name: string; breed: string | null; birthday: string | null; approximateAgeYears: number | null; bio: string | null; favoriteThing: string | null; ownerMembershipId: string | null; photoFile: FileReference | null; removePhoto: boolean; }
interface UpsertPostRequest { content: string; photoFiles: FileReference[]; removedPhotoIds: string[]; }

function optionalValue(value?: string): string | null {
  return value || null;
}

function optionalNumber(value?: string): number | null {
  if (!value) return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function mapRole(role: ApiRole): MembershipRole {
  if (role === 0 || role === 'Owner') return 'Owner';
  if (role === 1 || role === 'Admin') return 'Admin';
  return 'Member';
}

function mapFamily(family: ApiFamily): FamilySummary {
  return { id: family.id, name: family.name, slug: family.slug, description: family.description, role: mapRole(family.role), memberCount: family.memberCount, createdAt: family.createdAt };
}

function mapMember(member: ApiMember): FamilyMember {
  return { id: member.membershipId, userId: member.userId, displayName: member.displayName, avatarUrl: member.avatarUrl, bio: member.bio, role: mapRole(member.role), joinedAt: member.joinedAt };
}

function mapDog(dog: ApiDog): DogProfile {
  return {
    id: dog.id, familyId: dog.familyId, name: dog.name, photoUrl: dog.photoUrl, breed: dog.breed,
    birthday: dog.birthday, approximateAge: dog.approximateAgeYears == null ? null : `${dog.approximateAgeYears} years`,
    bio: dog.bio, favoriteThing: dog.favoriteThing, ownerMemberId: dog.ownerMembershipId,
    ownerDisplayName: dog.owner?.displayName, createdAt: dog.createdAt, canEdit: dog.canManage ?? false
  };
}

function mapReactionType(type: ApiReactionType): ReactionType {
  if (type === 0 || String(type).toLowerCase() === 'paw') return 'paw';
  if (type === 1 || String(type).toLowerCase() === 'heart') return 'heart';
  return 'bone';
}

function mapReactions(summary?: ApiReactionSummary): ReactionSummary[] {
  const mine = new Set((summary?.myReactions ?? []).map(mapReactionType));
  return (['paw', 'heart', 'bone'] as ReactionType[]).map((type) => ({
    type,
    count: summary?.counts?.[type] ?? summary?.counts?.[type[0].toUpperCase() + type.slice(1)] ?? 0,
    reactedByMe: mine.has(type)
  }));
}

function mapComment(comment: ApiComment): Comment {
  return { id: comment.id, postId: comment.postId, authorUserId: comment.author.id, authorDisplayName: comment.author.displayName, authorAvatarUrl: comment.author.avatarUrl, content: comment.content, createdAt: comment.createdAt, updatedAt: comment.updatedAt, canDelete: comment.canDelete };
}

function mapPost(post: ApiPost): Post {
  return {
    id: post.id, familyId: post.familyId, authorUserId: post.author.id, authorDisplayName: post.author.displayName,
    authorAvatarUrl: post.author.avatarUrl, content: post.content, createdAt: post.createdAt, updatedAt: post.updatedAt,
    isEdited: post.isEdited, canEdit: post.canEdit, canDelete: post.canDelete, photos: post.photos ?? [],
    comments: (post.comments ?? []).map(mapComment), commentCount: post.commentCount, reactions: mapReactions(post.reactions)
  };
}
