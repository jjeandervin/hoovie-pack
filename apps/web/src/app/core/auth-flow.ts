export function isFamilyInviteReturnUrl(value: string): boolean {
  if (!value.startsWith('/') || value.startsWith('//')) return false;

  try {
    const url = new URL(value, 'https://hooviepack.local');
    if (url.pathname !== '/onboarding') return false;

    return Boolean(url.searchParams.get('code')?.trim() || url.searchParams.get('invite')?.trim());
  } catch {
    return false;
  }
}
