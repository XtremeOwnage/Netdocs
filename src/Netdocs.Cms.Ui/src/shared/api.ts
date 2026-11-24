// Thin client for the CMS JSON API. Every call carries the per-run token the server put in the
// page, so other websites open in the browser cannot call the API.

export type Json = null | boolean | number | string | Json[] | { [key: string]: Json };
export type FrontMatter = Record<string, Json>;

export interface PageSummary {
  path: string;
  title: string;
  url: string | null;
  tags: string[];
  categories: string[];
  date: string | null;
  draft: boolean;
  inNav: boolean;
  size: number;
  modified: string;
}

export interface PageDocument {
  path: string;
  url: string | null;
  hash: string;
  frontMatter: FrontMatter;
  frontMatterOrder: string[];
  body: string;
  raw: string;
}

export interface RenderedBlock {
  index: number;
  kind: string;
  source: string;
  html: string;
}

export interface TermUsage { name: string; count: number; pages: string[] }
export interface SimilarTerms { names: string[]; reason: string }

export interface MediaItem {
  path: string;
  kind: 'image' | 'file';
  size: number;
  modified: string;
  width: number | null;
  height: number | null;
  references: number;
}

export interface WebpResult { from: string; to: string; oldSize: number; newSize: number; updatedPages: number; error: string | null }

export interface GitFile { path: string; index: string; workTree: string; originalPath: string | null }
export interface GitStatus { isRepository: boolean; branch: string | null; upstream: string | null; ahead: number; behind: number; files: GitFile[] }
export interface GitCommit { hash: string; author: string; date: string; subject: string }
export interface GitResult { ok: boolean; output: string }

export interface IconMatch { name: string; shortcode: string; svg: string }
export interface TemplateInfo { name: string; overridden: boolean; inTheme: boolean }

export interface SiteInfo {
  siteName: string;
  docsDir: string;
  configFile: string;
  projectRoot: string;
  customDir: string | null;
  features: string[];
  colors: string[];
  siteKeys: string[];
}

export class ApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

interface CmsConfig { token: string; api: string; admin: string; path: string | null }

function config(): CmsConfig {
  const injected = (window as unknown as { __netdocsCms?: CmsConfig }).__netdocsCms;
  if (injected) return injected;
  const meta = document.querySelector<HTMLMetaElement>('meta[name="netdocs-cms-token"]');
  return { token: meta?.content ?? '', api: '/__cms/api', admin: '/__cms/', path: null };
}

export const cms = config();

async function request<T>(method: string, path: string, body?: unknown, raw = false): Promise<T> {
  const headers: Record<string, string> = { 'X-Netdocs-Cms': cms.token };
  let payload: BodyInit | undefined;
  if (body instanceof FormData) payload = body;
  else if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    payload = JSON.stringify(body);
  }
  let response: Response;
  try {
    response = await fetch(cms.api + path, { method, headers, body: payload });
  } catch {
    // A dropped connection (for example while the server rebuilds): retry reads once.
    if (method !== 'GET') throw new ApiError('Could not reach netdocs serve. Is it still running?', 0);
    await new Promise((r) => setTimeout(r, 400));
    try {
      response = await fetch(cms.api + path, { method, headers });
    } catch {
      throw new ApiError('Could not reach netdocs serve. Is it still running?', 0);
    }
  }
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const data = await response.json();
      message = data.error ?? data.detail ?? data.title ?? message;
    } catch { /* not JSON */ }
    throw new ApiError(message, response.status);
  }
  if (raw) return (await response.text()) as T;
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

const q = (params: Record<string, string | number | boolean | null | undefined>) =>
  '?' + Object.entries(params)
    .filter(([, v]) => v !== null && v !== undefined)
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
    .join('&');

export const api = {
  site: () => request<SiteInfo>('GET', '/site'),

  pages: () => request<PageSummary[]>('GET', '/pages'),
  page: (path: string) => request<PageDocument>('GET', '/page' + q({ path })),
  pageByUrl: (url: string) => request<{ path: string }>('GET', '/page/by-url' + q({ url })),
  savePage: (path: string, hash: string | null, frontMatter: FrontMatter | null, body: string | null) =>
    request<PageDocument>('POST', '/page/save', { path, hash, frontMatter, body }),
  saveRaw: (path: string, hash: string | null, raw: string) => request<PageDocument>('POST', '/page/raw', { path, hash, raw }),
  createPage: (path: string, title: string, addToNav: boolean, frontMatter?: FrontMatter, body?: string) =>
    request<PageDocument>('POST', '/page/create', { path, title, addToNav, frontMatter, body }),
  movePage: (from: string, to: string) => request<{ path: string; updatedPages: number; navUpdated: boolean }>('POST', '/page/move', { from, to }),
  deletePage: (path: string) => request<void>('POST', '/page/delete', { path }),
  references: (path: string) => request<string[]>('GET', '/page/references' + q({ path })),
  blocks: (path: string) => request<{ page: PageDocument; blocks: RenderedBlock[] }>('GET', '/page/blocks' + q({ path })),
  saveBlocks: (path: string, hash: string, blocks: { keep?: number; source?: string }[], frontMatter: FrontMatter | null) =>
    request<{ page: PageDocument; blocks: RenderedBlock[] }>('POST', '/page/blocks/save', { path, hash, blocks, frontMatter }),
  render: (path: string, body: string, frontMatter: FrontMatter) =>
    request<{ html: string }>('POST', '/render', { path, body, frontMatter, blocks: false }),
  renderBlocks: (path: string, body: string, frontMatter: FrontMatter) =>
    request<{ blocks: RenderedBlock[] }>('POST', '/render', { path, body, frontMatter, blocks: true }),

  nav: () => request<{ nav: Json[]; pages: { path: string; title: string; inNav: boolean }[] }>('GET', '/nav'),
  saveNav: (nav: Json[]) => request<{ nav: Json[] }>('POST', '/nav', { nav }),

  taxonomy: (field: string) => request<{ field: string; terms: TermUsage[]; similar: SimilarTerms[]; singleUse: string[] }>('GET', '/taxonomy' + q({ field })),
  renameTerms: (field: string, from: string[], to: string) => request<{ changed: number }>('POST', '/taxonomy/rename', { field, from, to }),
  deleteTerms: (field: string, names: string[]) => request<{ changed: number }>('POST', '/taxonomy/delete', { field, names }),
  assignTerms: (field: string, pages: string[], add: string[], remove: string[]) =>
    request<{ changed: number }>('POST', '/taxonomy/assign', { field, pages, add, remove }),

  media: () => request<MediaItem[]>('GET', '/media'),
  mediaUrl: (path: string) => `${cms.api}/media/file${q({ path, t: cms.token })}`,
  icons: (query: string, limit = 120) => request<IconMatch[]>('GET', '/icons' + q({ q: query, limit })),
  upload: (folder: string, files: File[], overwrite: boolean) => {
    const form = new FormData();
    form.set('folder', folder);
    form.set('overwrite', String(overwrite));
    for (const f of files) form.append('files', f, f.name);
    return request<{ saved: string[] }>('POST', '/media/upload', form);
  },
  deleteMedia: (path: string, force: boolean) => request<void>('POST', '/media/delete', { path, force }),
  moveMedia: (from: string, to: string) => request<{ updatedPages: number }>('POST', '/media/move', { from, to }),
  webp: (paths: string[], quality: number, maxWidth: number | null) => request<WebpResult[]>('POST', '/media/webp', { paths, quality, maxWidth }),
  resize: (path: string, maxWidth: number) => request<{ size: number }>('POST', '/media/resize', { path, maxWidth }),

  gitStatus: () => request<GitStatus>('GET', '/git/status'),
  gitDiff: (path: string | null, staged: boolean) => request<{ diff: string }>('GET', '/git/diff' + q({ path, staged })),
  gitStage: (paths: string[]) => request<GitResult>('POST', '/git/stage', { paths }),
  gitUnstage: (paths: string[]) => request<GitResult>('POST', '/git/unstage', { paths }),
  gitDiscard: (paths: string[]) => request<GitResult>('POST', '/git/discard', { paths }),
  gitCommit: (message: string, stageAll: boolean) => request<GitResult>('POST', '/git/commit', { message, stageAll }),
  gitLog: (count = 30) => request<GitCommit[]>('GET', '/git/log' + q({ count })),
  gitBranches: () => request<{ current: string | null; local: string[]; remote: string[] }>('GET', '/git/branches'),
  gitCheckout: (branch: string, create: boolean) => request<GitResult>('POST', '/git/checkout', { branch, create }),
  gitPull: () => request<GitResult>('POST', '/git/pull'),
  gitPush: () => request<GitResult>('POST', '/git/push'),
  gitFetch: () => request<GitResult>('POST', '/git/fetch'),

  templates: () => request<TemplateInfo[]>('GET', '/templates'),
  template: (name: string) => request<{ name: string; text: string; original: string | null; overridden: boolean }>('GET', '/template' + q({ name })),
  saveTemplate: (name: string, text: string) => request<void>('POST', '/template', { name, text }),
  revertTemplate: (name: string) => request<void>('POST', '/template/revert', { name }),
  settings: () => request<Record<string, Json>>('GET', '/settings'),
  saveSettings: (settings: Record<string, Json>) => request<Record<string, Json>>('POST', '/settings', settings),
  configFile: () => request<{ text: string }>('GET', '/config'),
  saveConfigFile: (text: string) => request<void>('POST', '/config', { text }),
};

export const formatBytes = (n: number) =>
  n < 1024 ? `${n} B` : n < 1024 * 1024 ? `${(n / 1024).toFixed(1)} KB` : `${(n / 1024 / 1024).toFixed(1)} MB`;
