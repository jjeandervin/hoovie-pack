export function isUrlWithinBase(value: string, baseUrl: string, documentOrigin: string): boolean {
  try {
    const requestUrl = new URL(value, documentOrigin);
    const apiUrl = new URL(baseUrl || '/', documentOrigin);
    const apiPath = apiUrl.pathname.replace(/\/+$/, '');
    const isApiPath = apiPath.length === 0 ||
      requestUrl.pathname === apiPath ||
      requestUrl.pathname.startsWith(`${apiPath}/`);

    return requestUrl.origin === apiUrl.origin && isApiPath;
  } catch {
    return false;
  }
}
