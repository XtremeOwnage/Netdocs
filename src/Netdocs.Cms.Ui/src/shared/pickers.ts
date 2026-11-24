// Pickers shared by the admin app and the in-page editor: icons, images, links and page templates.
// They are plain DOM (no framework) so both bundles can use them, and they carry their own styles,
// which follow the admin app's colors or the Material palette of the page they open on.
import { api, type MediaItem } from './api';

const css = `
.ndp-overlay { position: fixed; inset: 0; z-index: 2147483600; background: rgb(0 0 0 / 0.45); display: flex; align-items: center; justify-content: center; padding: 16px; }
.ndp-dialog {
  --ndp-bg: var(--surface, var(--md-default-bg-color, #fff));
  --ndp-fg: var(--fg, var(--md-default-fg-color, #1f2328));
  --ndp-muted: var(--muted, var(--md-default-fg-color--light, #6b7280));
  --ndp-line: var(--line, var(--md-default-fg-color--lightest, #d0d7de));
  --ndp-accent: var(--accent, var(--md-primary-fg-color, #4051b5));
  --ndp-accent-fg: var(--accent-fg, var(--md-primary-bg-color, #fff));
  background: var(--ndp-bg); color: var(--ndp-fg); border: 1px solid var(--ndp-line); border-radius: 10px;
  box-shadow: 0 12px 40px rgb(0 0 0 / 0.35); width: min(760px, 100%); max-height: min(720px, calc(100vh - 32px));
  display: flex; flex-direction: column; font: 14px/1.4 -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
}
.ndp-head { display: flex; align-items: center; gap: 8px; padding: 12px 14px; border-bottom: 1px solid var(--ndp-line); }
.ndp-head h2 { margin: 0; font-size: 15px; font-weight: 600; flex: 1; color: inherit; }
.ndp-body { padding: 12px 14px; overflow: auto; flex: 1; display: flex; flex-direction: column; gap: 10px; }
.ndp-foot { display: flex; gap: 8px; justify-content: flex-end; padding: 10px 14px; border-top: 1px solid var(--ndp-line); }
.ndp-input { width: 100%; box-sizing: border-box; padding: 7px 9px; border: 1px solid var(--ndp-line); border-radius: 6px; background: transparent; color: inherit; font: inherit; }
.ndp-input:focus { outline: 2px solid var(--ndp-accent); outline-offset: -1px; }
.ndp-btn { padding: 6px 12px; border: 1px solid var(--ndp-line); border-radius: 6px; background: transparent; color: inherit; font: inherit; cursor: pointer; }
.ndp-btn:hover { border-color: var(--ndp-accent); }
.ndp-btn--primary { background: var(--ndp-accent); color: var(--ndp-accent-fg); border-color: var(--ndp-accent); }
.ndp-btn[disabled] { opacity: .5; cursor: default; }
.ndp-muted { color: var(--ndp-muted); font-size: 12px; }
.ndp-row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.ndp-row > .ndp-input { flex: 1; min-width: 140px; }
.ndp-label { display: flex; flex-direction: column; gap: 4px; font-size: 12px; font-weight: 600; flex: 1; min-width: 140px; }
.ndp-icons { display: grid; grid-template-columns: repeat(auto-fill, minmax(92px, 1fr)); gap: 6px; }
.ndp-icon { display: flex; flex-direction: column; align-items: center; gap: 4px; padding: 8px 4px; border: 1px solid var(--ndp-line); border-radius: 6px; background: transparent; color: inherit; cursor: pointer; font-size: 10px; overflow: hidden; }
.ndp-icon:hover, .ndp-icon.on { border-color: var(--ndp-accent); background: color-mix(in srgb, var(--ndp-accent) 12%, transparent); }
.ndp-icon svg { width: 26px; height: 26px; fill: currentColor; }
.ndp-icon span { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ndp-media { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: 8px; }
.ndp-thumb { display: flex; flex-direction: column; gap: 4px; padding: 4px; border: 2px solid transparent; border-radius: 6px; background: transparent; color: inherit; cursor: pointer; font-size: 11px; text-align: left; }
.ndp-thumb img { width: 100%; height: 90px; object-fit: contain; border-radius: 4px; background: repeating-conic-gradient(rgb(128 128 128 / .15) 0 25%, transparent 0 50%) 0 0 / 16px 16px; }
.ndp-thumb span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ndp-thumb.on, .ndp-thumb:hover { border-color: var(--ndp-accent); }
.ndp-list { display: flex; flex-direction: column; }
.ndp-list button { text-align: left; padding: 6px 8px; border: 0; border-radius: 4px; background: transparent; color: inherit; font: inherit; cursor: pointer; }
.ndp-list button:hover, .ndp-list button.on { background: color-mix(in srgb, var(--ndp-accent) 14%, transparent); }
.ndp-list small { color: var(--ndp-muted); margin-left: 6px; }
`;

let styled = false;
function injectStyles() {
  if (styled) return;
  styled = true;
  const style = document.createElement('style');
  style.textContent = css;
  document.head.append(style);
}

const h = <K extends keyof HTMLElementTagNameMap>(tag: K, attrs: Record<string, string> = {}, ...children: (Node | string)[]) => {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === 'class') node.className = v;
    else node.setAttribute(k, v);
  }
  node.append(...children);
  return node;
};

const btn = (label: string, onClick: () => void, cls = '') => {
  const b = h('button', { type: 'button', class: `ndp-btn ${cls}` }, label);
  b.addEventListener('click', (e) => { e.preventDefault(); onClick(); });
  return b;
};

/** Opens a modal and resolves with whatever `done` is called with (null when cancelled). */
function dialog<T>(title: string, build: (done: (v: T | null) => void) => { body: HTMLElement; actions: HTMLElement[] }): Promise<T | null> {
  injectStyles();
  return new Promise((resolve) => {
    const overlay = h('div', { class: 'ndp-overlay' });
    const close = (v: T | null) => { overlay.remove(); document.removeEventListener('keydown', onKey, true); resolve(v); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.stopPropagation(); close(null); } };
    const { body, actions } = build(close);
    const box = h('div', { class: 'ndp-dialog', role: 'dialog', 'aria-label': title },
      h('div', { class: 'ndp-head' }, h('h2', {}, title), btn('✕', () => close(null))),
      h('div', { class: 'ndp-body' }, body),
      h('div', { class: 'ndp-foot' }, btn('Cancel', () => close(null)), ...actions),
    );
    overlay.append(box);
    overlay.addEventListener('mousedown', (e) => { if (e.target === overlay) close(null); });
    document.addEventListener('keydown', onKey, true);
    document.body.append(overlay);
    box.querySelector<HTMLInputElement>('input')?.focus();
  });
}

// --- Paths --------------------------------------------------------------------------------------

/** The link from docs-relative file `from` to docs-relative file `to`, e.g. `../images/a.webp`. */
export function relativePath(from: string, to: string): string {
  const a = from.split('/').slice(0, -1);
  const b = to.split('/');
  let i = 0;
  while (i < a.length && i < b.length - 1 && a[i] === b[i]) i++;
  return [...a.slice(i).map(() => '..'), ...b.slice(i)].join('/');
}

const folderOf = (path: string) => path.split('/').slice(0, -1).join('/');

// --- Icons --------------------------------------------------------------------------------------

/**
 * Lets the user search the bundled icon sets. Resolves with the front matter name
 * (`material/home`), '' to clear the icon, or null when cancelled.
 */
export function openIconPicker(current: string, title = 'Choose an icon'): Promise<string | null> {
  return dialog<string>(title, (done) => {
    const search = h('input', { class: 'ndp-input', type: 'search', placeholder: 'Search icons: home, github, alert, rocket…' }) as HTMLInputElement;
    search.value = current.replace(/^(material|octicons|fontawesome|simple)\//, '').replace(/\//g, ' ');
    const grid = h('div', { class: 'ndp-icons' });
    const note = h('div', { class: 'ndp-muted' });
    let timer = 0;
    let seq = 0;
    const load = async () => {
      const mine = ++seq;
      note.textContent = 'Searching…';
      try {
        const icons = await api.icons(search.value.trim(), 160);
        if (mine !== seq) return;
        grid.replaceChildren(...icons.map((i) => {
          const b = h('button', { type: 'button', class: `ndp-icon${i.name === current ? ' on' : ''}`, title: i.name });
          b.innerHTML = i.svg;
          b.append(h('span', {}, i.name.replace(/^material\//, '')));
          b.addEventListener('click', () => done(i.name));
          return b;
        }));
        note.textContent = icons.length ? `${icons.length === 160 ? 'First 160 matches' : `${icons.length} icons`} · Material, Octicons, FontAwesome and Simple Icons` : 'No icons match.';
      } catch (e) {
        note.textContent = e instanceof Error ? e.message : String(e);
      }
    };
    search.addEventListener('input', () => { clearTimeout(timer); timer = window.setTimeout(load, 200); });
    search.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); grid.querySelector<HTMLButtonElement>('button')?.click(); } });
    void load();
    return { body: h('div', { class: 'ndp-body', style: 'padding:0' }, search, note, grid), actions: current ? [btn('Remove icon', () => done(''))] : [] };
  });
}

// --- Images -------------------------------------------------------------------------------------

export interface PickedImage { src: string; alt: string; width: string; caption: string; path: string }

/** Lets the user pick (or upload) an image. `src` is relative to `pagePath`. */
export function openImagePicker(pagePath: string, initial: Partial<PickedImage> = {}): Promise<PickedImage | null> {
  return dialog<PickedImage>(initial.path ? 'Image' : 'Insert an image', (done) => {
    let items: MediaItem[] = [];
    let chosen: string | null = initial.path ?? null;
    const search = h('input', { class: 'ndp-input', type: 'search', placeholder: 'Filter images…' }) as HTMLInputElement;
    const grid = h('div', { class: 'ndp-media' });
    const note = h('div', { class: 'ndp-muted' }, 'Loading images…');
    const alt = h('input', { class: 'ndp-input', placeholder: 'What the image shows (for screen readers and search)' }) as HTMLInputElement;
    alt.value = initial.alt ?? '';
    const width = h('input', { class: 'ndp-input', placeholder: 'e.g. 400 (optional)' }) as HTMLInputElement;
    width.value = initial.width ?? '';
    const caption = h('input', { class: 'ndp-input', placeholder: 'Shown under the image (optional)' }) as HTMLInputElement;
    caption.value = initial.caption ?? '';
    const file = h('input', { type: 'file', accept: 'image/*', multiple: '', style: 'display:none' }) as HTMLInputElement;
    const folder = h('input', { class: 'ndp-input', placeholder: 'Folder' }) as HTMLInputElement;
    folder.value = folderOf(pagePath) ? `${folderOf(pagePath)}/images` : 'images';
    const ok = btn('Insert', () => {
      if (!chosen) return;
      done({ path: chosen, src: relativePath(pagePath, chosen), alt: alt.value.trim(), width: width.value.trim(), caption: caption.value.trim() });
    }, 'ndp-btn--primary');
    if (initial.path) ok.textContent = 'Apply';

    const draw = () => {
      const term = search.value.toLowerCase();
      const shown = items.filter((m) => !term || m.path.toLowerCase().includes(term)).slice(0, 300);
      grid.replaceChildren(...shown.map((m) => {
        const b = h('button', { type: 'button', class: `ndp-thumb${m.path === chosen ? ' on' : ''}`, title: m.path },
          h('img', { src: api.mediaUrl(m.path), alt: '', loading: 'lazy' }),
          h('span', {}, m.path.split('/').pop() ?? m.path));
        b.addEventListener('click', () => { chosen = m.path; ok.toggleAttribute('disabled', false); draw(); });
        b.addEventListener('dblclick', () => { chosen = m.path; ok.click(); });
        return b;
      }));
      note.textContent = items.length ? `${shown.length} of ${items.length} images${shown.length === 300 ? ' (type to narrow down)' : ''}` : 'No images yet. Upload one.';
    };
    const load = async () => {
      items = (await api.media()).filter((m) => m.kind === 'image');
      draw();
    };
    file.addEventListener('change', async () => {
      if (!file.files?.length) return;
      note.textContent = 'Uploading…';
      try {
        const { saved } = await api.upload(folder.value.trim(), [...file.files], false);
        chosen = saved[0] ?? chosen;
        await load();
        ok.toggleAttribute('disabled', !chosen);
      } catch (e) {
        note.textContent = e instanceof Error ? e.message : String(e);
      }
      file.value = '';
    });
    search.addEventListener('input', draw);
    ok.toggleAttribute('disabled', !chosen);
    void load().catch((e) => { note.textContent = e instanceof Error ? e.message : String(e); });

    const body = h('div', { class: 'ndp-body', style: 'padding:0' },
      h('div', { class: 'ndp-row' }, search, folder, btn('⬆ Upload…', () => file.click()), file),
      note, grid,
      h('div', { class: 'ndp-row' },
        h('label', { class: 'ndp-label' }, 'Description (alt text)', alt),
      ),
      h('div', { class: 'ndp-row' },
        h('label', { class: 'ndp-label' }, 'Width in pixels', width),
        h('label', { class: 'ndp-label' }, 'Caption', caption),
      ),
    );
    return { body, actions: [ok] };
  });
}

/** Markdown for a picked image: a plain image, or a figure when it has a caption. */
export function imageMarkdown(img: PickedImage): string {
  const attrs = img.width ? `{ width="${img.width.replace(/\D/g, '')}" }` : '';
  const image = `![${img.alt.replace(/[[\]]/g, '')}](${encodeURI(img.src)})${attrs}`;
  if (!img.caption) return image;
  return `^^^\n${image}\n^^^ ${img.caption.replace(/\n/g, ' ')}`;
}

/** Reads an image or figure block back into picker fields, or null when it is something else. */
export function parseImageMarkdown(source: string, pagePath: string): PickedImage | null {
  const s = source.trim();
  const fig = /^<figure markdown(?:="1")?>\s*\n\s*(!\[[^\]]*\]\([^)]+\)(?:\{[^}]*\})?)\s*\n\s*<figcaption>([\s\S]*?)<\/figcaption>\s*\n<\/figure>$/.exec(s)
    ?? /^\^{3,}\s*\n\s*(!\[[^\]]*\]\([^)]+\)(?:\{[^}]*\})?)\s*\n\^{3,}[ \t]*(.*)$/.exec(s);
  const imageText = fig ? fig[1] : s;
  const m = /^!\[([^\]]*)\]\(([^)\s]+)\)(\{[^}]*\})?$/.exec(imageText);
  if (!m) return null;
  const src = decodeURI(m[2]);
  if (/^[a-z]+:|^\//i.test(src)) return null;
  const width = /width="?(\d+)/.exec(m[3] ?? '')?.[1] ?? '';
  const parts: string[] = [];
  for (const seg of [...folderOf(pagePath).split('/').filter(Boolean), ...src.split('/')]) {
    if (seg === '..') parts.pop();
    else if (seg !== '.') parts.push(seg);
  }
  return { src, alt: m[1], width, caption: fig ? fig[2].replace(/&lt;/g, '<') : '', path: parts.join('/') };
}

// --- Links --------------------------------------------------------------------------------------

/** Lets the user pick a page of the site or type a URL. Resolves with the link target. */
export function openLinkPicker(pagePath: string, current = ''): Promise<string | null> {
  return dialog<string>('Link to…', (done) => {
    const url = h('input', { class: 'ndp-input', placeholder: 'Search pages, or paste a URL (https://…)' }) as HTMLInputElement;
    url.value = current;
    const list = h('div', { class: 'ndp-list' });
    let pages: { path: string; title: string }[] = [];
    let selected = -1;
    const shown = () => {
      const term = url.value.toLowerCase();
      return pages.filter((p) => !term || p.title.toLowerCase().includes(term) || p.path.toLowerCase().includes(term)).slice(0, 60);
    };
    const draw = () => {
      list.replaceChildren(...shown().map((p, i) => {
        const b = h('button', { type: 'button', class: i === selected ? 'on' : '' }, p.title, h('small', {}, p.path));
        b.addEventListener('click', () => done(relativePath(pagePath, p.path)));
        return b;
      }));
    };
    url.addEventListener('input', () => { selected = -1; draw(); });
    url.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowDown') { selected = Math.min(selected + 1, shown().length - 1); draw(); e.preventDefault(); }
      if (e.key === 'ArrowUp') { selected = Math.max(selected - 1, -1); draw(); e.preventDefault(); }
      if (e.key === 'Enter') {
        e.preventDefault();
        const pick = shown()[selected];
        if (pick) done(relativePath(pagePath, pick.path));
        else if (url.value.trim()) done(url.value.trim());
      }
    });
    void api.pages().then((p) => { pages = p.map((x) => ({ path: x.path, title: x.title })); draw(); });
    return {
      body: h('div', { class: 'ndp-body', style: 'padding:0' }, url, h('div', { class: 'ndp-muted' }, 'Pick a page, or press Enter to use what you typed as the link.'), list),
      actions: [btn('Use as link', () => { if (url.value.trim()) done(url.value.trim()); }, 'ndp-btn--primary')],
    };
  });
}

// --- Templates ----------------------------------------------------------------------------------

/** Page templates a page can choose in its front matter (`template:`). */
export async function pageTemplates(): Promise<string[]> {
  try {
    const all = await api.templates();
    return all.map((t) => t.name).filter((n) => !n.includes('/') && n.endsWith('.html') && n !== '404.html' && n !== 'export.html');
  } catch {
    return ['main.html'];
  }
}
