export class ApiError extends Error {
  constructor(
    message: string,
    readonly requiresAssurance: boolean,
    readonly status?: number,
  ) {
    super(message);
  }
}
// Only the fixed same-origin BFF/API requests use this hint. Requirements remain server-owned.
export function assuranceRequired(response: Response): boolean {
  return (
    response.status === 401 &&
    /^(Bearer|DPoP)\s/i.test(response.headers.get('WWW-Authenticate') || '') &&
    /(?:^|[,\s])error="insufficient_user_authentication"(?:,|$)/.test(
      response.headers.get('WWW-Authenticate') || '',
    )
  );
}
export type Member = { id: string; organizationId: string; costCentre: string; roles: string[] };
export type Catalogue = {
  company: { displayName: string; notice: string };
  organizations: { id: string; displayName: string }[];
  products: { id: string; name: string; priceChf: number }[];
};
export const money = (value: number) =>
  new Intl.NumberFormat('de-CH', {
    style: 'currency',
    currency: 'CHF',
    maximumFractionDigits: 0,
  }).format(value);
export async function api<T>(path: string, body?: unknown, idempotencyKey?: string): Promise<T> {
  return (await apiResponse<T>(path, body, idempotencyKey)).data;
}
// Browser requests carry the same-origin session cookie. The BFF injects the OAuth token and
// any DPoP proof on its server-side API request. The custom header participates in the SDK's
// CSRF protection; it is neither a credential nor a substitute for API authorization.
export async function apiResponse<T>(
  path: string,
  body?: unknown,
  idempotencyKey?: string,
): Promise<{ data: T; status: number }> {
  const response = await fetch(path, {
    credentials: 'same-origin',
    redirect: 'error',
    ...(body === undefined
      ? {}
      : {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'X-ProAuth-BFF': '1',
            ...(idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {}),
          },
          body: JSON.stringify(body),
        }),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new ApiError(
      problem.title || `Request failed (${response.status})`,
      assuranceRequired(response),
      response.status,
    );
  }
  return { data: await response.json(), status: response.status };
}
