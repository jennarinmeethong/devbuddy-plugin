import { request, type APIRequestContext, type APIResponse } from "@playwright/test";
import type { OperationName, Operations } from "../../../web/admin/src/api/operations";
import { env } from "./env";

/**
 * The HTTP API, as a signed-in person reaches it.
 *
 * Operation names and shapes come from the client generated for `web/admin`, so a test that calls
 * an operation that no longer exists, or with arguments it no longer takes, fails to type-check
 * rather than failing at run time with a 400.
 */

export interface TokenPair {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

export interface Me {
  userId: string;
  email: string;
  displayName: string;
  workspaces: {
    workspaceId: string;
    name: string;
    role: string;
    scopedToProject: string | null;
    permissions: string[];
  }[];
}

export class ApiFailure extends Error {
  constructor(
    readonly operation: string,
    readonly status: number,
    readonly title: string,
    readonly detail: string,
  ) {
    super(`${operation} answered ${status} ${title}: ${detail}`);
    this.name = "ApiFailure";
  }

  static async from(operation: string, response: APIResponse): Promise<ApiFailure> {
    let title = response.statusText();
    let detail = "";

    try {
      const body = (await response.json()) as { title?: string; detail?: string };
      title = body.title ?? title;
      detail = body.detail ?? "";
    } catch {
      /* Not every failure carries a problem document. */
    }

    return new ApiFailure(operation, response.status(), title, detail);
  }
}

export async function anonymous(): Promise<APIRequestContext> {
  return request.newContext({ baseURL: env.baseURL });
}

export async function signInForTokens(
  http: APIRequestContext,
  email: string,
  password: string,
): Promise<TokenPair> {
  const response = await http.post("/auth/sign-in", { data: { email, password } });

  if (!response.ok()) {
    throw await ApiFailure.from("sign-in", response);
  }

  return (await response.json()) as TokenPair;
}

export class Api {
  private constructor(
    private readonly http: APIRequestContext,
    private tokens: TokenPair,
  ) {}

  static async signIn(email: string, password: string): Promise<Api> {
    const http = await anonymous();
    return new Api(http, await signInForTokens(http, email, password));
  }

  get accessToken(): string {
    return this.tokens.accessToken;
  }

  get refreshToken(): string {
    return this.tokens.refreshToken;
  }

  /** The raw response, for tests whose point is the refusal. */
  async call(name: string, args: unknown): Promise<APIResponse> {
    return this.authorised((headers) => this.http.post(`/operations/${name}`, { data: args, headers }));
  }

  async invoke<N extends OperationName>(
    name: N,
    args: Operations[N]["arguments"],
  ): Promise<Operations[N]["result"]> {
    const response = await this.call(name, args);

    if (!response.ok()) {
      throw await ApiFailure.from(name, response);
    }

    return (await response.json()) as Operations[N]["result"];
  }

  async get(path: string): Promise<APIResponse> {
    return this.authorised((headers) => this.http.get(path, { headers }));
  }

  async upload(path: string, file: { name: string; mimeType: string; buffer: Buffer }, description: string) {
    return this.authorised((headers) =>
      this.http.post(path, { headers, multipart: { file, description } }),
    );
  }

  async me(): Promise<Me> {
    const response = await this.get("/me");

    if (!response.ok()) {
      throw await ApiFailure.from("me", response);
    }

    return (await response.json()) as Me;
  }

  async dispose(): Promise<void> {
    await this.http.dispose();
  }

  /** One retry after a refresh, the same thing the web client does when an access token lapses. */
  private async authorised(
    send: (headers: Record<string, string>) => Promise<APIResponse>,
  ): Promise<APIResponse> {
    let response = await send({ authorization: `Bearer ${this.tokens.accessToken}` });

    if (response.status() === 401) {
      const refreshed = await this.http.post("/auth/refresh", {
        data: { refreshToken: this.tokens.refreshToken },
      });

      if (refreshed.ok()) {
        this.tokens = (await refreshed.json()) as TokenPair;
        response = await send({ authorization: `Bearer ${this.tokens.accessToken}` });
      }
    }

    return response;
  }
}
