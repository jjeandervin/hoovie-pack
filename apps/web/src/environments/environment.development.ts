export const environment = {
  production: false,
  runtimeConfigUrl: null as string | null,
  runtimeConfig: {
    apiBaseUrl: 'http://localhost:5103/api',
    oidcIssuer: 'http://localhost:8081/realms/hooviepack',
    oidcClientId: 'hooviepack-web',
    oidcRedirectUri: 'http://localhost:4200/auth/callback',
    oidcPostLogoutRedirectUri: 'http://localhost:4200/login'
  }
};
