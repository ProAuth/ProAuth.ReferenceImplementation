// Observe this page's real JavaScript boundary. This is not a scan of all browser memory.
// Never render storage or cookie values; even a non-OAuth cookie could be a credential.
export function inspectBrowserVisibility() {
  const readableCookieNames = document.cookie
    .split(';')
    .map((cookie) => cookie.trim().split('=')[0])
    .filter(Boolean)
    .sort();
  return {
    localStorageKeys: Object.keys(localStorage).sort(),
    sessionStorageKeys: Object.keys(sessionStorage).sort(),
    readableCookieNames,
    oauthTokenSlots: {
      accessToken: !!localStorage.getItem('mervikon_access'),
      refreshToken: !!localStorage.getItem('mervikon_refresh'),
    },
    sessionCookieReadable: readableCookieNames.includes('__Host-mervikon-browser-lab'),
  };
}
