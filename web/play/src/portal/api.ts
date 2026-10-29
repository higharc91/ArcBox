import type { CampaignResponse, ErrorResponse } from "../protocol";
import type { StoredProfile } from "../session/storage";

/** What a portal API call came back with. `status` 0 means the request never reached the server. */
export type ApiResult<T> =
  | { ok: true; status: number; body: T }
  | { ok: false; status: number; error: ErrorResponse | null };

async function call<T>(method: string, url: string, key: StoredProfile, body?: unknown): Promise<ApiResult<T>> {
  const headers: Record<string, string> = { Authorization: `Profile ${key.key}` };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  let response: Response;
  try {
    response = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    return { ok: false, status: 0, error: null };
  }
  const parsed = await response.json().catch(() => null); // 401, 403 and 429 have no body
  if (response.ok) return { ok: true, status: response.status, body: parsed as T };
  const error = parsed && typeof parsed === "object" && typeof (parsed as ErrorResponse).error === "string" ? (parsed as ErrorResponse) : null;
  return { ok: false, status: response.status, error };
}

const campaignUrl = (code: string) => `/api/campaigns/${encodeURIComponent(code)}`;

/**
 * Loads the campaign with the first of `keys` (most recent first) that gets through. The search moves on from a 401
 * (the server doesn't know the key) and a 403 (a valid key whose profile isn't a member, e.g. someone else on a shared
 * tablet) and stops at anything else (200, 404, 429, a network failure, 5xx). `key` is the key that answered. When no
 * key got through: if some key got a 403, that last 403 and its key; otherwise a null key and a 401.
 */
export async function getCampaign(
  code: string,
  keys: StoredProfile[],
): Promise<{ key: StoredProfile | null; result: ApiResult<CampaignResponse> }> {
  let forbidden: { key: StoredProfile; result: ApiResult<CampaignResponse> } | null = null;
  for (const key of keys) {
    const result = await call<CampaignResponse>("GET", campaignUrl(code), key);
    if (result.status === 403) forbidden = { key, result };
    else if (result.status !== 401) return { key, result };
  }
  return forbidden ?? { key: null, result: { ok: false, status: 401, error: null } };
}

/** Runs a portal command against the campaign `version` the phone last saw. */
export function sendCommand(
  code: string,
  key: StoredProfile,
  type: string,
  data: unknown,
  version: number,
): Promise<ApiResult<CampaignResponse>> {
  return call<CampaignResponse>("POST", `${campaignUrl(code)}/commands`, key, { type, data, version });
}
