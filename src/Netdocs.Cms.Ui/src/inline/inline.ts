// In-page editor. `netdocs serve` adds this script to every page: it shows a small CMS toolbar
// and, in edit mode, turns the article into editable blocks. Plain text blocks are edited in place;
// admonitions, tabs, code, diagrams and anything else open a form or the block's markdown source.
// Saving sends only the changed blocks, so untouched markdown stays byte-for-byte the same.
import './inline.css';
import { api, cms, ApiError, type FrontMatter, type GeneratedInfo, type Json, type PageDocument, type RenderedBlock } from '../shared/api';
import {
  admonitionTypes, codeLanguages, formatAdmonition, formatFence, formatTabs, htmlToMarkdown, insertTemplates,
  needsSourceEditing, parseAdmonition, parseFence, parseTabs, richKinds,
} from '../shared/markdown';
import { imageMarkdown, openIconPicker, openImagePicker, openLinkPicker, pageTemplates, parseImageMarkdown } from '../shared/pickers';
import {
  formatExpr, formatTimeline, fromIsoDate, parseExpr, parseTimeline, pickerFormat, toIsoDate,
  type Timeline, type TimelineExclusion, type TimelineInput, type TimelineOutput,
} from '../shared/timeline';

interface EditorBlock {
  /** Index of the original block in the file, or null for a new block. */
  original: number | null;
  /** Every file block this one stands for, when several were joined (a grid's opening tag, its
   * content and its closing tag). */
  span?: number[];
  kind: string;
  source: string;
  html: string;
  /** True once the block's markdown differs from the file. */
  changed: boolean;
  rich: boolean;
  el?: HTMLElement;
  content?: HTMLElement;
  renderedHtml?: string;
}

interface State {
  page: PageDocument;
  blocks: EditorBlock[];
  frontMatter: FrontMatter;
  frontMatterChanged: boolean;
  article: HTMLElement;
  originalArticle: string;
  reloadPending: boolean;
  /** Number of blocks in the file when it was loaded. */
  originalCount: number;
  /** Set when a plugin adds content to this page at build time. */
  generated: GeneratedInfo | null;
}

let state: State | null = null;
let bar: HTMLElement;
let status: HTMLElement;
/** The page is shown inside the admin app's page editor: always in edit mode, docked toolbar. */
const embedded = new URLSearchParams(location.search).has('cms-embed');
/** The block the user last worked in; toolbar inserts go after it. */
let activeIndex: number | null = null;

/** Tells the admin app (when the page is embedded in it) whether there are unsaved changes. */
function notifyParent(event: 'status' | 'saved' = 'status') {
  if (!state && event === 'status') return;
  if (!embedded || window.parent === window) return;
  try {
    window.parent.postMessage({ source: 'netdocs-cms', event, path: state?.page.path ?? cms.path, dirty: isDirty() }, location.origin);
  } catch { /* parent gone */ }
}

const el = <K extends keyof HTMLElementTagNameMap>(tag: K, attrs: Record<string, string> = {}, ...children: (Node | string)[]) => {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === 'class') node.className = v;
    else node.setAttribute(k, v);
  }
  node.append(...children);
  return node;
};

const button = (label: string, title: string, onClick: (e: MouseEvent) => void, cls = '') => {
  const b = el('button', { type: 'button', class: `ndcms-btn ${cls}`, title });
  b.innerHTML = label;
  b.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); onClick(e); });
  return b;
};

function findArticle(): HTMLElement | null {
  return document.querySelector<HTMLElement>('article.md-content__inner')
    ?? document.querySelector<HTMLElement>('.md-content article')
    ?? document.querySelector<HTMLElement>('main article')
    ?? document.querySelector<HTMLElement>('main');
}

// --- Toolbar ----------------------------------------------------------------------------------

function mountToolbar() {
  // The template editor's preview shows the page exactly as visitors see it.
  if (new URLSearchParams(location.search).has('cms-preview')) return;
  bar = el('div', { class: `ndcms-bar${embedded ? ' ndcms-bar--embedded' : ''}`, role: 'toolbar', 'aria-label': 'Netdocs CMS' });
  if (embedded) document.documentElement.classList.add('ndcms-embedded');
  (window as unknown as { __netdocsCmsDirty: () => boolean }).__netdocsCmsDirty = () => isDirty();
  (window as unknown as { __netdocsCmsSave: () => Promise<void> }).__netdocsCmsSave = () => save();
  if (embedded) {
    // Links to other pages open that page in the admin editor instead of inside this frame.
    document.addEventListener('click', (e) => {
      const a = (e.target as HTMLElement).closest?.('a[href]') as HTMLAnchorElement | null;
      if (!a || e.defaultPrevented || a.closest('.ndcms-block, .ndcms-panel, .ndcms-bar, .ndp-overlay')) return;
      const url = new URL(a.href, location.href);
      if (url.origin !== location.origin || url.pathname === location.pathname) return;
      e.preventDefault();
      if (isDirty() && !confirm('Leave this page and lose your unsaved changes?')) return;
      window.parent.postMessage({ source: 'netdocs-cms', event: 'navigate', url: url.pathname, dirty: false }, location.origin);
    }, true);
  }
  status = el('span', { class: 'ndcms-status' });
  renderToolbar();
  document.body.append(bar);
  keepFullPageLoads();

  document.addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's' && state) {
      e.preventDefault();
      void save();
    }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'e' && !state && cms.path) {
      e.preventDefault();
      void enterEditMode();
    }
  });
  window.addEventListener('beforeunload', (e) => {
    if (isDirty() && !embedded) { e.preventDefault(); e.returnValue = ''; }
  });

  // While editing, the dev server's live reload waits until the editor is closed.
  (window as unknown as { __netdocsCmsHoldReload: () => boolean }).__netdocsCmsHoldReload = () => {
    if (!state) return false;
    state.reloadPending = true;
    return true;
  };

  const params = new URLSearchParams(location.search);
  if (cms.path && (embedded || sessionStorage.getItem('ndcms-edit') === cms.path || params.has('cms-edit'))) {
    sessionStorage.removeItem('ndcms-edit');
    if (params.has('cms-edit')) {
      params.delete('cms-edit');
      history.replaceState(null, '', location.pathname + (params.size ? '?' + params : '') + location.hash);
    }
    void enterEditMode();
  }
}

function renderToolbar() {
  bar.replaceChildren();
  bar.classList.toggle('ndcms-bar--editing', !!state);
  if (!state) {
    if (cms.path) bar.append(button('✎ Edit page', 'Edit this page in place (Ctrl+E)', () => void enterEditMode(), 'ndcms-btn--primary'));
    else bar.append(el('span', { class: 'ndcms-muted' }, 'Generated page'));
    const link = el('a', { class: 'ndcms-btn', href: cms.admin + (cms.path ? `#/pages/edit?path=${encodeURIComponent(cms.path)}` : '#/'), title: 'Open the CMS dashboard' }, '☰ CMS');
    bar.append(link);
    return;
  }
  const quick = el('div', { class: 'ndcms-bar__insert', role: 'group', 'aria-label': 'Insert' },
    el('span', { class: 'ndcms-bar__label' }, 'Insert'),
    ...quickInserts.map((q) => button(`<b>${q.icon}</b>${q.short ? ` <span>${q.short}</span>` : ''}`, `Insert ${q.label.toLowerCase()} after the selected block`, () => void insertTemplate(insertPosition(), q.id), 'ndcms-btn--tool')),
    moreMenu(),
  );
  bar.append(
    ...(embedded ? [] : [el('span', { class: 'ndcms-path', title: state.page.path }, state.page.path)]),
    quick,
    el('span', { class: 'ndcms-bar__spacer' }),
    status,
    button('⚙ Page settings', 'Title, date, tags, icon, template', () => void openSettings()),
    button('↶ Discard', 'Throw away unsaved changes', () => void discard()),
    button('💾 Save', 'Save changes (Ctrl+S)', () => void save(), 'ndcms-btn--primary'),
    ...(embedded ? [] : [button('✕', 'Leave edit mode', () => void exitEditMode())]),
  );
  updateStatus();
}

function updateStatus(message?: string, kind: 'info' | 'error' | 'ok' = 'info') {
  if (!status) return;
  status.className = `ndcms-status ndcms-status--${kind}`;
  status.textContent = message ?? (isDirty() ? 'Unsaved changes' : 'No changes');
  notifyParent();
}

// Toolbar shortcuts: the most used blocks, one click each. "More" lists everything.
const quickInserts = [
  { id: 'p', icon: '¶', label: 'Text', short: 'Text' },
  { id: 'h2', icon: 'H2', label: 'Heading', short: '' },
  { id: 'h3', icon: 'H3', label: 'Subheading', short: '' },
  { id: 'ul', icon: '•', label: 'Bulleted list', short: 'List' },
  { id: 'ol', icon: '1.', label: 'Numbered list', short: 'List' },
  { id: 'task', icon: '☑', label: 'Task list', short: 'Tasks' },
  { id: 'image', icon: '🖼', label: 'Image', short: 'Image' },
  { id: 'table', icon: '▦', label: 'Table', short: 'Table' },
  { id: 'note', icon: 'ⓘ', label: 'Callout box', short: 'Callout' },
  { id: 'tabs', icon: '⊟', label: 'Tabs', short: 'Tabs' },
  { id: 'code', icon: '{ }', label: 'Code block', short: 'Code' },
  { id: 'quote', icon: '❝', label: 'Quote', short: 'Quote' },
  { id: 'hr', icon: '―', label: 'Divider', short: 'Line' },
];

function insertPosition(): number {
  if (!state) return 0;
  return activeIndex === null || activeIndex >= state.blocks.length ? state.blocks.length : activeIndex + 1;
}

function moreMenu(): HTMLElement {
  const wrap = el('div', { class: 'ndcms-more' });
  const menu = blockMenu(() => insertPosition(), () => wrap.classList.remove('ndcms-more--open'));
  const toggle = button('More ▾', 'All block types', () => {
    const open = wrap.classList.toggle('ndcms-more--open');
    if (open) menu.querySelector<HTMLInputElement>('input')?.focus();
  }, 'ndcms-btn--tool');
  document.addEventListener('mousedown', (e) => { if (!wrap.contains(e.target as Node)) wrap.classList.remove('ndcms-more--open'); });
  wrap.append(toggle, menu);
  return wrap;
}

/** A searchable list of every block type; picking one inserts it at `position()`. */
function blockMenu(position: () => number, onPick: () => void, pick?: (id: string) => void): HTMLElement {
  const menu = el('div', { class: 'ndcms-insert__menu' });
  const filter = el('input', { type: 'search', placeholder: 'Find a block type…', class: 'ndcms-input' }) as HTMLInputElement;
  const list = el('div', { class: 'ndcms-insert__list' });
  const draw = () => {
    list.replaceChildren();
    const term = filter.value.toLowerCase();
    let group = '';
    for (const t of insertTemplates.filter((x) => !term || x.label.toLowerCase().includes(term) || x.group.toLowerCase().includes(term))) {
      if (t.group !== group) { group = t.group; list.append(el('div', { class: 'ndcms-insert__group' }, group)); }
      list.append(button(t.label, t.label, () => { onPick(); if (pick) pick(t.id); else void insertTemplate(position(), t.id); }, 'ndcms-insert__item'));
    }
  };
  filter.addEventListener('input', draw);
  filter.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); list.querySelector<HTMLButtonElement>('.ndcms-insert__item')?.click(); }
    if (e.key === 'Escape') onPick();
  });
  draw();
  menu.append(filter, list);
  return menu;
}

function isDirty(): boolean {
  if (!state) return false;
  syncRichBlocks();
  return state.frontMatterChanged
    || state.blocks.some((b) => b.changed || b.original === null)
    || orderChanged();
}

function orderChanged(): boolean {
  if (!state) return false;
  // A joined block (a grid) stands for several file blocks in a row.
  const originals = state.blocks.flatMap((b) => (b.original === null ? [null] : b.span ?? [b.original]));
  return originals.some((o, i) => o !== i) || originals.length !== state.originalCount;
}

// --- Edit mode --------------------------------------------------------------------------------

async function enterEditMode() {
  const article = findArticle();
  if (!article || !cms.path) return;
  updateStatus('Loading…');
  try {
    const { page, blocks, generated } = await api.blocks(cms.path);
    state = {
      page,
      blocks: mergeWrappers(blocks).map(toEditorBlock),
      frontMatter: structuredClone(page.frontMatter),
      frontMatterChanged: false,
      article,
      originalArticle: article.innerHTML,
      reloadPending: false,
      originalCount: blocks.length,
      generated,
    };
    document.documentElement.classList.add('ndcms-editing');
    renderToolbar();
    renderBlocks();
    attachMetadataEditors();
  } catch (e) {
    alertError(e);
  }
}

/** The blog post sidebar (author, date, category) and the page title open the page settings. */
function attachMetadataEditors() {
  for (const target of document.querySelectorAll<HTMLElement>('.nd-post-sidebar, .md-content__inner > header')) {
    if (target.classList.contains('ndcms-meta')) continue;
    target.classList.add('ndcms-meta');
    target.title = 'Click to edit the post details: date, authors, categories…';
    const edit = button('✎ Edit details', 'Edit the date, authors, categories and other page settings', () => void openSettings(), 'ndcms-meta__btn');
    target.prepend(edit);
    target.addEventListener('click', (e) => {
      if (!state) return;
      e.preventDefault();
      e.stopPropagation();
      void openSettings();
    }, true);
  }
}

function toEditorBlock(b: RenderedBlock & { span?: number[] }): EditorBlock {
  const rich = isRich(b.kind, b.source, b.html);
  return { original: b.index, span: b.span, kind: b.kind, source: b.source, html: b.html, changed: false, rich };
}

const wrapperOpen = /^<(div|section|article|aside)\b[^>]*\bmarkdown\b[^>]*>\s*$/;

/** What a whole `<div … markdown>…</div>` block is: a card grid, a grid, or another section. */
function wrapperKind(source: string): string | null {
  const t = source.trim();
  const open = /^<(div|section|article|aside)\b[^>]*\bmarkdown\b[^>]*>/.exec(t);
  if (!open || !t.endsWith(`</${open[1]}>`)) return null;
  if (/class="grid cards"/.test(open[0])) return 'cards';
  if (/class="grid"/.test(open[0])) return 'grid';
  return 'section';
}

/**
 * Joins a `<div class="grid" markdown>` opening tag, the blocks inside it and its closing tag into
 * one block, so a grid is moved, deleted and edited as a whole.
 */
function mergeWrappers<T extends { index: number; kind: string; source: string; html: string }>(blocks: T[]): (T & { span?: number[] })[] {
  const out: (T & { span?: number[] })[] = [];
  for (let i = 0; i < blocks.length; i++) {
    const open = wrapperOpen.exec(blocks[i].source.trim());
    if (!open || blocks[i].kind !== 'html') { out.push(blocks[i]); continue; }
    const tag = open[1];
    let depth = 0;
    let end = -1;
    for (let j = i; j < blocks.length; j++) {
      const src = blocks[j].source;
      depth += (src.match(new RegExp(`<${tag}\\b`, 'g')) ?? []).length - (src.match(new RegExp(`</${tag}>`, 'g')) ?? []).length;
      if (depth <= 0) { end = j; break; }
    }
    if (end < 0) { out.push(blocks[i]); continue; }
    const parts = blocks.slice(i, end + 1);
    const source = parts.map((b) => b.source).join('\n\n');
    out.push({ ...blocks[i], source, html: parts.map((b) => b.html).join('\n'), kind: wrapperKind(source) ?? 'section', span: parts.map((b) => b.index) });
    i = end;
  }
  return out;
}

/**
 * The editor and its toolbar belong to the page they were loaded with. Material's instant
 * navigation swaps the page content in place, which would leave the toolbar on the old page
 * (in edit mode, with nothing editable). So links to other pages load the page in full, and
 * anything else that swaps the page (Back, Forward, search results) reloads it.
 */
function keepFullPageLoads() {
  const loadedPath = location.pathname;
  if (!embedded) {
    window.addEventListener('click', (e) => {
      const a = (e.target as HTMLElement).closest?.('a[href]') as HTMLAnchorElement | null;
      if (!a || e.defaultPrevented || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
      if (a.target && a.target !== '_self') return;
      if (a.closest('.ndcms-block, .ndcms-panel, .ndcms-bar, .ndcms-meta, .ndp-overlay, .ndcms-modal')) return;
      const url = new URL(a.href, location.href);
      if (url.origin !== location.origin || url.pathname === location.pathname) return;
      if (isDirty() && !confirm('Leave this page and lose your unsaved changes?')) { e.preventDefault(); e.stopImmediatePropagation(); return; }
      // Keep the theme's instant navigation from seeing the click; the browser follows the link.
      e.stopImmediatePropagation();
      if (state) state.reloadPending = false;
      state = null;
    }, true);
  }
  const w = window as unknown as { document$?: { subscribe: (f: () => void) => unknown } };
  try {
    w.document$?.subscribe(() => {
      if (location.pathname !== loadedPath || (state && !state.article.isConnected)) location.reload();
    });
  } catch { /* the theme's scripts are optional */ }
}

async function exitEditMode(force = false) {
  if (!state || embedded) return;
  if (!force && isDirty() && !confirm('Leave edit mode and lose your unsaved changes?')) return;
  const reload = state.reloadPending || state.blocks.some((b) => b.changed);
  state.article.innerHTML = state.originalArticle;
  state = null;
  document.documentElement.classList.remove('ndcms-editing');
  closeAllPanels();
  renderToolbar();
  if (reload) location.reload();
}

async function discard() {
  if (!state) return;
  if (isDirty() && !confirm('Discard all unsaved changes on this page?')) return;
  const path = state.page.path;
  state = null;
  sessionStorage.setItem('ndcms-edit', path);
  location.reload();
}

// --- Rendering blocks -------------------------------------------------------------------------

function renderBlocks() {
  if (!state) return;
  const s = state;
  s.article.replaceChildren();
  s.article.append(inserter(0));
  s.blocks.forEach((block, i) => {
    s.article.append(renderBlock(block, i));
    s.article.append(inserter(i + 1));
  });
  if (s.blocks.length === 0 && !s.generated) {
    s.article.replaceChildren(el('div', { class: 'ndcms-empty' },
      el('p', {}, 'This page is empty. Start with:'),
      el('div', { class: 'ndcms-empty__choices' }, ...quickInserts.slice(0, 10).map((q) => button(`<span>${q.icon}</span> ${q.label}`, `Add ${q.label.toLowerCase()}`, () => void insertTemplate(0, q.id), 'ndcms-empty__choice'))),
    ));
  } else {
    const add = button('+ Add a block', 'Add a block at the end of the page', () => {
      const open = end.classList.toggle('ndcms-insert--open');
      if (open) end.querySelector<HTMLInputElement>('input')?.focus();
    }, 'ndcms-add-end__btn');
    const end = el('div', { class: 'ndcms-insert ndcms-add-end' }, add, blockMenu(() => s.blocks.length, () => end.classList.remove('ndcms-insert--open')));
    s.article.lastElementChild?.remove();
    s.article.append(end);
  }
  if (s.generated) {
    // Where the plugin's output goes: at the tags marker, else after the page's own text.
    const marker = s.blocks.findIndex((b) => /<!--\s*material\/tags/.test(b.source));
    const banner = generatedBanner(s.generated, s.originalArticle);
    const at = marker >= 0 ? s.blocks[marker].el : null;
    if (at) at.after(banner); else s.article.lastElementChild?.before(banner);
  }
  markActive();
  refreshThemeWidgets(s.article);
  updateStatus();
}

/** Stands in for content a plugin adds at build time, which is not part of the page's markdown. */
function generatedBanner(info: GeneratedInfo, originalHtml: string): HTMLElement {
  const snapshot = el('div', { class: 'ndcms-generated__snapshot md-typeset', 'aria-hidden': 'true' });
  snapshot.innerHTML = originalHtml;
  snapshot.querySelectorAll('h1, .ndcms-bar, script, .md-content__button').forEach((n) => n.remove());
  if (!snapshot.textContent?.trim()) snapshot.hidden = true;
  snapshot.querySelectorAll('a, button, input').forEach((n) => n.setAttribute('tabindex', '-1'));
  const settings = el('a', { class: 'ndcms-btn', href: `${cms.admin}#/plugins?name=${encodeURIComponent(info.plugin)}`, target: embedded ? '_top' : '_self' }, `${info.label} plugin settings ›`);
  return el('div', { class: 'ndcms-generated' },
    el('div', { class: 'ndcms-generated__head' },
      el('strong', {}, `⚙ Made by the ${info.label} plugin`),
      el('span', { class: 'ndcms-muted' }, ` ${info.what[0].toUpperCase() + info.what.slice(1)} is added here when the site builds, so it can't be edited as text. Add your own text above or below it, or change what it shows in the plugin settings.`),
      settings,
    ),
    snapshot,
  );
}

function renderBlock(block: EditorBlock, index: number): HTMLElement {
  const wrapper = el('div', { class: `ndcms-block ndcms-block--${block.kind}${block.changed || block.original === null ? ' ndcms-block--changed' : ''}`, 'data-index': String(index) });
  const content = el('div', { class: 'ndcms-block__content' });
  content.innerHTML = block.html || `<p class="ndcms-muted">(${block.kind} block with no visible output)</p>`;
  block.el = wrapper;
  block.content = content;

  const tools = el('div', { class: 'ndcms-block__tools' },
    el('span', { class: 'ndcms-block__kind' }, label(block.kind)),
  );
  if (!block.rich) tools.append(button('✎', 'Edit this block', () => openBlockEditor(index)));
  tools.append(
    button('&lt;/&gt;', 'Edit the markdown source', () => openBlockEditor(index, true)),
    button('↑', 'Move up', () => move(index, -1)),
    button('↓', 'Move down', () => move(index, 1)),
    button('⧉', 'Duplicate', () => duplicate(index)),
    button('🗑', 'Delete block', () => remove(index), 'ndcms-btn--danger'),
  );

  // Task list boxes can be ticked right on the page.
  content.querySelectorAll<HTMLInputElement>('input[type="checkbox"]').forEach((box) => {
    box.removeAttribute('disabled');
    box.classList.add('ndcms-task');
    box.title = box.checked ? 'Mark as not done' : 'Mark as done';
  });
  const onTask = (e: Event) => {
    const box = (e.target as HTMLElement).closest?.('input[type="checkbox"]');
    if (!box || !content.contains(box)) return;
    e.preventDefault();
    e.stopPropagation();
    if (e.type !== 'click') return;
    const n = [...content.querySelectorAll('input[type="checkbox"]')].indexOf(box);
    void toggleTask(index, n);
  };
  content.addEventListener('mousedown', onTask, true);
  content.addEventListener('click', onTask, true);

  // Captured after the tweaks above, so they do not count as an edit.
  block.renderedHtml = content.innerHTML;

  wrapper.addEventListener('focusin', () => { activeIndex = index; markActive(); });
  wrapper.addEventListener('mousedown', () => { activeIndex = index; markActive(); });

  if (block.rich) {
    content.contentEditable = 'true';
    content.spellcheck = true;
    content.addEventListener('focus', () => showFormatBar(content));
    listKeys(content);
    content.addEventListener('input', () => {
      wrapper.classList.add('ndcms-block--changed');
      updateStatus('Unsaved changes');
    });
    // Links must not navigate while editing.
    content.addEventListener('click', (e) => {
      const t = e.target as HTMLElement;
      if (t.closest('a')) e.preventDefault();
      // A block that is just an image opens the image picker.
      if (t.tagName === 'IMG' && !t.classList.contains('twemoji') && state && parseImageMarkdown(block.source, state.page.path)) {
        e.preventDefault();
        openBlockEditor(index);
      }
    });
  } else {
    wrapper.classList.add('ndcms-block--structured');
    // On the wrapper, not just the content: selecting a block can shift it a little under the
    // pointer, so the click may land on the block's padding.
    wrapper.addEventListener('click', (e) => {
      // Let tabs, collapsibles and copy buttons work; anything else opens the editor.
      const t = e.target as HTMLElement;
      if (t.closest('label, summary, button, input, .tabbed-labels, .ndcms-block__tools')) return;
      e.preventDefault();
      openBlockEditor(index);
    });
  }
  wrapper.append(tools, content);
  return wrapper;
}

const label = (kind: string) => ({
  paragraph: 'Text', heading: 'Heading', list: 'List', quote: 'Quote', table: 'Table', code: 'Code', html: 'HTML',
  hr: 'Divider', admonition: 'Admonition', tabs: 'Tabs', footnotes: 'Footnote', references: 'Link references',
  abbreviations: 'Abbreviations', math: 'Math', figure: 'Image', cards: 'Card grid', grid: 'Grid', section: 'Section', mixed: 'Blocks',
} as Record<string, string>)[kind] ?? kind;

function inserter(position: number): HTMLElement {
  const wrap = el('div', { class: 'ndcms-insert' });
  const menu = blockMenu(() => position, () => wrap.classList.remove('ndcms-insert--open'));
  const toggle = button('+', 'Add a block here', () => {
    const open = wrap.classList.toggle('ndcms-insert--open');
    if (open) menu.querySelector<HTMLInputElement>('input')?.focus();
  }, 'ndcms-insert__toggle');
  wrap.append(el('span', { class: 'ndcms-insert__line' }), toggle, menu);
  return wrap;
}

/** Flips the n-th task box of a block in its markdown: `- [ ]` ⇄ `- [x]`. */
async function toggleTask(index: number, n: number) {
  if (!state) return;
  syncRichBlocks();
  const block = state.blocks[index];
  let seen = -1;
  const lines = block.source.split('\n').map((line) => line.replace(/^(\s*(?:>\s*)*(?:[-*+]|\d+[.)])\s+)\[( |x|X)\]/, (m, lead: string, mark: string) => {
    seen++;
    return seen === n ? `${lead}[${mark === ' ' ? 'x' : ' '}]` : m;
  }));
  if (seen < n) return;
  block.source = lines.join('\n');
  block.changed = true;
  activeIndex = index;
  try {
    await renderSource(block);
  } catch (e) {
    alertError(e);
    return;
  }
  renderBlocks();
}

function markActive() {
  if (!state) return;
  state.blocks.forEach((b, i) => b.el?.classList.toggle('ndcms-block--active', i === activeIndex));
}

/** Inserts one of the block templates. Images and tables open their editor first. */
async function insertTemplate(position: number, id: string) {
  if (!state) return;
  const template = insertTemplates.find((t) => t.id === id);
  if (!template) return;
  if (id === 'image' || id === 'figure') {
    const picked = await openImagePicker(state.page.path);
    if (!picked) return;
    await insert(position, imageMarkdown(picked), false);
    return;
  }
  await insert(position, template.markdown);
}

async function insert(position: number, markdown: string, openEditor = true) {
  if (!state) return;
  syncRichBlocks();
  const block: EditorBlock = { original: null, kind: 'new', source: markdown, html: '', changed: true, rich: false };
  try {
    await renderSource(block);
  } catch (e) {
    alertError(e);
    return;
  }
  state.blocks.splice(position, 0, block);
  activeIndex = position;
  renderBlocks();
  block.el?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  if (!block.rich) {
    if (openEditor) openBlockEditor(position);
    return;
  }
  if (block.content) selectPlaceholder(block.content);
}

function move(index: number, delta: number) {
  if (!state) return;
  const target = index + delta;
  if (target < 0 || target >= state.blocks.length) return;
  syncRichBlocks();
  const [b] = state.blocks.splice(index, 1);
  state.blocks.splice(target, 0, b);
  renderBlocks();
}

function duplicate(index: number) {
  if (!state) return;
  syncRichBlocks();
  const b = state.blocks[index];
  state.blocks.splice(index + 1, 0, { ...b, original: null, changed: true, el: undefined, content: undefined });
  renderBlocks();
}

function remove(index: number) {
  if (!state) return;
  syncRichBlocks();
  state.blocks.splice(index, 1);
  renderBlocks();
}

/** Copies visual edits back into each rich block's markdown. */
function syncRichBlocks() {
  if (!state) return;
  for (const b of state.blocks) {
    if (!b.rich || !b.content) continue;
    if (b.content.innerHTML === b.renderedHtml) continue;
    const md = htmlToMarkdown(b.content.innerHTML);
    b.renderedHtml = b.content.innerHTML;
    if (md !== b.source) {
      b.source = md;
      b.html = b.content.innerHTML;
      b.changed = true;
    }
  }
}

/** Renders a block's markdown through the site pipeline. */
async function renderSource(block: EditorBlock) {
  if (!state) return;
  // Footnote markers need their definitions to render; send the page's along and drop their output.
  const refs = [...block.source.matchAll(/\[\^([^\]]+)\](?!:)/g)].map((m) => m[1]);
  const defs = refs.length ? state.blocks.filter((b) => b !== block && refs.some((r) => b.source.includes(`[^${r}]:`))).map((b) => b.source) : [];
  const rendered = (await api.renderBlocks(state.page.path, [block.source, ...defs].join('\n\n'), state.frontMatter)).blocks;
  const blocks = defs.length ? rendered.filter((b) => b.kind !== 'footnotes') : rendered;
  block.html = blocks.map((b) => b.html).join('\n');
  const kinds = blocks.map((b) => b.kind);
  block.kind = wrapperKind(block.source) ?? (kinds.length === 1 ? kinds[0] : kinds.length === 0 ? 'empty' : 'mixed');
  block.rich = kinds.length === 1 && isRich(block.kind, block.source, block.html);
}

/** Text-like blocks are edited in place; tables, images and the rest use a form. */
function isRich(kind: string, source: string, html: string): boolean {
  if (kind === 'table') return false;
  if (parseImageMarkdown(source, state?.page.path ?? cms.path ?? '') || parseImageBlock(source)) return false;
  // A block that is one link gets the link form.
  if (parseLinkBlock(source)) return false;
  return richKinds.has(kind) && !needsSourceEditing(kind, source, html);
}

/** Lets Material's own scripts (tabs, code copy, tooltips, diagrams) pick up re-rendered content. */
function refreshThemeWidgets(root: HTMLElement) {
  const w = window as unknown as { document$?: { next: (d: Document) => void }; mermaid?: { run?: (o: unknown) => Promise<void> }; hljs?: { highlightElement: (e: Element) => void } };
  try { w.document$?.next(document); } catch { /* theme scripts are optional */ }
  // Diagrams with their own config (.nd-mermaid) are left to the theme's own renderer, which
  // does not force the site palette on them.
  try { w.mermaid?.run?.({ nodes: root.querySelectorAll('.mermaid:not([data-processed]):not(.nd-mermaid)') }); } catch { /* optional */ }
  try { root.querySelectorAll('pre code').forEach((c) => w.hljs?.highlightElement(c)); } catch { /* optional */ }
}

// --- Rich text formatting bar ------------------------------------------------------------------

let formatBar: HTMLElement | null = null;

/** Tab and Shift+Tab indent and outdent the list item the cursor is in. */
function listKeys(target: HTMLElement) {
  target.addEventListener('keydown', (e) => {
    if (e.key !== 'Tab' || e.ctrlKey || e.metaKey || e.altKey) return;
    if (indentListItem(target, e.shiftKey)) e.preventDefault();
  });
}

/**
 * Moves the list item at the cursor one level in (into a list under the item above it) or out
 * (after its parent item, taking the items below it along as its own sub-list). Returns false
 * when the cursor is not in a list item, so Tab keeps its usual meaning there.
 */
function indentListItem(target: HTMLElement, outdent: boolean): boolean {
  const sel = window.getSelection();
  if (!sel || !sel.rangeCount) return false;
  const range = sel.getRangeAt(0);
  const start = range.startContainer instanceof Element ? range.startContainer : range.startContainer.parentElement;
  const li = start?.closest('li');
  if (!li || !target.contains(li)) return false;
  const list = li.parentElement!;
  const saved = { sc: range.startContainer, so: range.startOffset, ec: range.endContainer, eo: range.endOffset };
  if (!outdent) {
    const prev = li.previousElementSibling;
    if (!prev || prev.tagName !== 'LI') return true;
    let sub = prev.lastElementChild;
    if (!sub || (sub.tagName !== 'UL' && sub.tagName !== 'OL')) {
      sub = document.createElement(list.tagName.toLowerCase());
      prev.append(sub);
    }
    sub.append(li);
  } else {
    const parentLi = list.parentElement;
    if (!parentLi || parentLi.tagName !== 'LI' || !target.contains(parentLi)) return true;
    const rest: Element[] = [];
    for (let n = li.nextElementSibling; n; n = n.nextElementSibling) rest.push(n);
    if (rest.length) {
      let sub = li.lastElementChild;
      if (!sub || (sub.tagName !== 'UL' && sub.tagName !== 'OL')) {
        sub = document.createElement(list.tagName.toLowerCase());
        li.append(sub);
      }
      sub.append(...rest);
    }
    parentLi.after(li);
    if (!list.children.length) list.remove();
  }
  try {
    const r = document.createRange();
    r.setStart(saved.sc, saved.so);
    r.setEnd(saved.ec, saved.eo);
    sel.removeAllRanges();
    sel.addRange(r);
  } catch { /* the cursor stays where the browser put it */ }
  target.dispatchEvent(new Event('input', { bubbles: true }));
  return true;
}

function showFormatBar(target: HTMLElement, host: Element | null = target.closest('.ndcms-block')) {
  formatBar?.remove();
  const exec = (cmd: string, value?: string) => { target.focus(); document.execCommand(cmd, false, value); target.dispatchEvent(new Event('input')); };
  // Pickers move focus away; remember the selection so the result lands where the cursor was.
  const keepSelection = () => {
    const sel = window.getSelection();
    const range = sel && sel.rangeCount && target.contains(sel.getRangeAt(0).commonAncestorContainer) ? sel.getRangeAt(0).cloneRange() : null;
    return () => {
      target.focus();
      if (range) { sel?.removeAllRanges(); sel?.addRange(range); }
    };
  };
  formatBar = el('div', { class: 'ndcms-format' },
    button('<b>B</b>', 'Bold (Ctrl+B)', () => exec('bold')),
    button('<i>I</i>', 'Italic (Ctrl+I)', () => exec('italic')),
    button('<s>S</s>', 'Strikethrough', () => exec('strikeThrough')),
    button('<code>{}</code>', 'Inline code', () => wrapSelection('code', target)),
    button('<mark>H</mark>', 'Highlight', () => wrapSelection('mark', target)),
    button('🔗', 'Link to a page or URL', async () => {
      const restore = keepSelection();
      const url = await openLinkPicker(state?.page.path ?? cms.path ?? '');
      restore();
      if (!url) return;
      const sel = window.getSelection();
      if (sel && sel.isCollapsed) {
        const a = document.createElement('a');
        a.href = url;
        a.setAttribute('data-md-href', url);
        a.textContent = url.replace(/\.md$/, '').split('/').pop() || url;
        sel.getRangeAt(0).insertNode(a);
        target.dispatchEvent(new Event('input'));
      } else {
        exec('createLink', url);
        target.querySelectorAll<HTMLAnchorElement>('a').forEach((a) => { if (a.getAttribute('href') === url) a.setAttribute('data-md-href', url); });
      }
    }),
    button('☺', 'Insert an icon', async () => {
      const restore = keepSelection();
      const name = await openIconPicker('', 'Insert an icon');
      restore();
      if (!name) return;
      const icons = await api.icons(name.split('/').pop() ?? name, 40).catch(() => []);
      const icon = icons.find((i) => i.name === name);
      const code = ':' + name.replace(/\//g, '-') + ':';
      const span = document.createElement('span');
      span.className = 'twemoji';
      span.title = code;
      span.innerHTML = icon?.svg ?? code;
      window.getSelection()?.getRangeAt(0).insertNode(span);
      target.dispatchEvent(new Event('input'));
    }),
    button('•', 'Bulleted list', () => exec('insertUnorderedList')),
    button('1.', 'Numbered list', () => exec('insertOrderedList')),
    button('⇥', 'Indent list item (Tab)', () => { target.focus(); indentListItem(target, false); }),
    button('⇤', 'Outdent list item (Shift+Tab)', () => { target.focus(); indentListItem(target, true); }),
    ...[2, 3, 4, 5, 6].map((n) => button('H' + n, `Heading ${n}`, () => exec('formatBlock', 'h' + n), 'ndcms-format__h')),
    button('¶', 'Paragraph', () => exec('formatBlock', 'p')),
    button('⌫', 'Clear formatting', () => exec('removeFormat')),
  );
  // Keep the cursor in the text while the bar's buttons are pressed.
  formatBar.addEventListener('mousedown', (e) => e.preventDefault());
  host?.prepend(formatBar);
  target.addEventListener('blur', () => {
    setTimeout(() => { if (!formatBar?.contains(document.activeElement)) { formatBar?.remove(); formatBar = null; } }, 150);
    syncRichBlocks();
    updateStatus();
  }, { once: true });
}

/** Wraps the selection in `tag`, or removes `tag` when the selection is already inside one
 * (so Highlight and Code toggle off the way Bold does). */
function wrapSelection(tag: string, target: HTMLElement) {
  const sel = window.getSelection();
  if (!sel || sel.rangeCount === 0) return;
  const range = sel.getRangeAt(0);
  if (!target.contains(range.commonAncestorContainer)) return;
  const start = range.startContainer instanceof Element ? range.startContainer : range.startContainer.parentElement;
  const around = start?.closest(tag);
  const inside = [...target.querySelectorAll(tag)].filter((n) => range.intersectsNode(n));
  if ((around && target.contains(around)) || inside.length) {
    for (const n of new Set([...(around && target.contains(around) ? [around] : []), ...inside])) {
      const parent = n.parentNode!;
      while (n.firstChild) parent.insertBefore(n.firstChild, n);
      parent.removeChild(n);
      parent.normalize();
    }
    target.dispatchEvent(new Event('input'));
    return;
  }
  if (sel.isCollapsed) return;
  const node = document.createElement(tag);
  node.appendChild(range.extractContents());
  range.insertNode(node);
  target.dispatchEvent(new Event('input'));
}

// --- Side panel: block editors and page settings -------------------------------------------------

/** Open panels, newest last. A block inside a callout or tab opens its own panel on top. */
const panels: { el: HTMLElement; flush: (() => void) | null }[] = [];

/** Closes the top panel (applying its last change) and shows the one under it. */
function closePanel() {
  const top = panels.pop();
  if (!top) return;
  top.flush?.();
  top.el.remove();
  const under = panels[panels.length - 1];
  if (under) under.el.style.display = '';
}

function closeAllPanels() {
  while (panels.length) closePanel();
}

/**
 * Opens the side panel. With `live`, every change in the panel is applied to the page after a
 * short pause, so there is no Apply step: the panel's last state is applied when it closes.
 * A `nested` panel stacks on the open one and returns to it when done.
 */
let escapeBound = false;

function openPanel(title: string, body: HTMLElement, actions: HTMLElement[], live?: () => void | Promise<void>, nested = false) {
  if (!nested) closeAllPanels();
  const under = panels[panels.length - 1];
  if (under) under.el.style.display = 'none';
  let timer = 0;
  const run = () => { clearTimeout(timer); timer = 0; void live?.(); };
  const schedule = () => { if (!live) return; clearTimeout(timer); timer = window.setTimeout(run, 450); };
  const back = under ? (under.el.querySelector('h2')?.textContent ?? 'the previous panel') : '';
  const done = button(under ? `‹ Back to ${back}` : 'Done', live ? 'Changes show on the page as you type. Close the panel.' : 'Close', closePanel, 'ndcms-btn--primary');
  const root = el('aside', { class: `ndcms-panel${under ? ' ndcms-panel--nested' : ''}`, role: 'dialog', 'aria-label': title },
    el('header', { class: 'ndcms-panel__head' },
      ...(under ? [button('‹', `Back to ${back}`, closePanel, 'ndcms-panel__back')] : []),
      el('h2', {}, title),
      ...(under ? [el('span', { class: 'ndcms-muted ndcms-panel__crumb' }, `in ${back}`)] : []),
      button('✕', under ? `Back to ${back} (Esc)` : 'Close (Esc)', closePanel)),
    el('div', { class: 'ndcms-panel__body' }, body),
    el('footer', { class: 'ndcms-panel__foot' }, ...(live ? [el('span', { class: 'ndcms-muted ndcms-live' }, '● Live'), ...actions, done] : actions)),
  );
  const entry = { el: root, flush: null as (() => void) | null };
  if (live) {
    for (const type of ['input', 'change']) body.addEventListener(type, schedule);
    // Buttons such as "Add tab" or "Remove" change the form without an input event.
    body.addEventListener('click', (e) => { if ((e.target as HTMLElement).closest('button')) setTimeout(schedule); }, true);
    entry.flush = () => { if (timer) run(); };
  }
  root.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); closePanel(); } });
  if (!escapeBound) {
    escapeBound = true;
    // Esc also works when the focus is on the page rather than in the panel. Dialogs and menus
    // opened from a panel (pickers, the insert menu) close first.
    document.addEventListener('keydown', (e) => {
      if (e.key !== 'Escape' || e.defaultPrevented || !panels.length) return;
      const t = e.target as HTMLElement;
      if (t.closest?.('.ndcms-panel, .ndcms-insert--open, .ndp-overlay, .ndcms-modal') || document.querySelector('.ndp-overlay, .ndcms-modal')) return;
      e.preventDefault();
      closePanel();
    });
  }
  panels.push(entry);
  document.body.append(root);
  root.querySelector<HTMLElement>('input, textarea, select')?.focus();
}

/** A labelled form row. Only a single control goes in a <label>: a click anywhere in a label
 * presses its first button, which would open menus inside a block list. */
const field = (labelText: string, control: HTMLElement, hint?: string) =>
  el(/^(INPUT|SELECT|TEXTAREA)$/.test(control.tagName) ? 'label' : 'div', { class: 'ndcms-field' }, el('span', { class: 'ndcms-field__label' }, labelText), control, ...(hint ? [el('small', { class: 'ndcms-muted' }, hint)] : []));

const textarea = (value: string, rows = 10, code = true) => {
  const t = el('textarea', { class: `ndcms-input${code ? ' ndcms-input--code' : ''}`, rows: String(rows), spellcheck: code ? 'false' : 'true' }) as HTMLTextAreaElement;
  t.value = value;
  // Tab indents (admonition and tab bodies are indented markdown).
  t.addEventListener('keydown', (e) => {
    if (e.key === 'Tab' && !e.shiftKey) {
      e.preventDefault();
      const { selectionStart: s, selectionEnd: en } = t;
      t.setRangeText('    ', s, en, 'end');
    }
  });
  return t;
};

const input = (value: string, placeholder = '') => {
  const i = el('input', { class: 'ndcms-input', placeholder }) as HTMLInputElement;
  i.value = value;
  return i;
};

const select = (value: string, options: string[]) => {
  const s = el('select', { class: 'ndcms-input' }) as HTMLSelectElement;
  for (const o of options) s.append(el('option', { value: o }, o));
  if (!options.includes(value)) s.append(el('option', { value }, value));
  s.value = value;
  return s;
};

function openBlockEditor(index: number, forceSource = false) {
  if (!state) return;
  syncRichBlocks();
  const block = state.blocks[index];
  const s = state;
  let seq = 0;
  /** Puts new markdown into the block and redraws just that block. */
  const apply = async (source: string, close = false) => {
      if (close) closePanel();
    if (source.trim() === block.source.trim()) return;
    block.source = source;
    block.changed = true;
    const mine = ++seq;
    try {
      await renderSource(block);
    } catch (e) {
      alertError(e);
      return;
    }
    if (mine !== seq || s !== state) return;
    const i = s.blocks.indexOf(block);
    if (i < 0) return;
    const old = block.el;
    const fresh = renderBlock(block, i);
    if (old?.isConnected) { old.replaceWith(fresh); markActive(); refreshThemeWidgets(fresh); updateStatus(); } else renderBlocks();
  };
  editBlock({
    source: block.source, kind: block.kind, nested: false,
    apply: (md) => apply(md),
    toSource: () => { closePanel(); openBlockEditor(index, true); },
  }, forceSource);
}

/** What a block editor works on: a block of the page, or a block inside a callout, tab or card. */
interface EditCtx {
  source: string;
  kind: string;
  apply: (md: string) => Promise<void>;
  /** Reopens the block in the markdown editor. */
  toSource: () => void;
  /** Opened from another panel: it stacks on top, and Done goes back to that panel. */
  nested: boolean;
}

/** Opens the form that fits a block's markdown, or its markdown source. */
function editBlock(ctx: EditCtx, forceSource = false) {
  if (!state) return;
  const s = state;
  const apply = ctx.apply;
  const live = (compute: () => string) => () => apply(compute());

  const imageBlock = !forceSource ? parseImageBlock(ctx.source) : null;
  if (imageBlock) {
    openImageEditor(ctx, imageBlock);
    return;
  }
  const image = !forceSource ? parseImageMarkdown(ctx.source, s.page.path) : null;
  if (image) {
    void openImagePicker(s.page.path, image).then((picked) => { if (picked) void apply(imageMarkdown(picked)); });
    return;
  }
  const cards = !forceSource ? parseCardGrid(ctx.source) : null;
  if (cards) {
    openCardGridEditor(ctx, cards);
    return;
  }
  const grid = !forceSource && !cards ? parsePlainGrid(ctx.source) : null;
  if (grid) {
    const body = blockList(grid.body, s.page.path);
    openPanel('Grid', el('div', {},
      el('p', { class: 'ndcms-muted' }, 'Each block below is one cell of the grid. The cells line up in columns as the screen allows.'),
      field('Cells', body.root),
    ), [
      button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
    ], live(() => `${grid.open}\n\n${body.value()}\n\n</div>`), ctx.nested);
    return;
  }
  const table = !forceSource && ctx.kind === 'table' ? parseTable(ctx.source) : null;
  if (table) {
    const editor = tableEditor(table);
    openPanel('Table', editor.root, [
      button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
    ], live(() => formatTable(editor.value())), ctx.nested);
    return;
  }

  const admonition = !forceSource ? parseAdmonition(ctx.source) : null;
  const fence = !forceSource && !admonition ? parseFence(ctx.source) : null;
  const tabs = !forceSource && !admonition && !fence ? parseTabs(ctx.source) : null;

  if (admonition) {
    const type = admonitionTypePicker(admonition.type);
    const style = select(admonition.marker, ['!!!', '???', '???+']);
    const title = input(admonition.title ?? '', 'Leave empty for the default title');
    const noTitle = el('input', { type: 'checkbox' }) as HTMLInputElement;
    noTitle.checked = admonition.title === '';
    const body = blockList(admonition.body, s.page.path);
    openPanel('Callout box', el('div', {},
      field('Type', type.root),
      field('Style', style, '!!! always open · ??? collapsed · ???+ collapsible, open'),
      field('Title', title),
      el('label', { class: 'ndcms-check' }, noTitle, ' Hide the title bar'),
      field('Content', body.root),
    ), [
      button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
    ], live(() => formatAdmonition({
      marker: style.value as '!!!', type: type.value(), extra: admonition.extra,
      title: noTitle.checked ? '' : title.value.trim() ? title.value : null, body: body.value(),
    })), ctx.nested);
    return;
  }

  if (fence && fence.lang === 'mermaid') {
    openMermaidEditor(ctx, fence);
    return;
  }

  const timeline = fence && fence.lang === 'timeline' ? parseTimeline(fence.code) : null;
  if (fence && timeline) {
    openTimelineEditor(ctx, fence, timeline);
    return;
  }

  if (fence) {
    const lang = input(fence.lang, 'Start typing: python, yaml, bash, mermaid…');
    const langList = 'ndcms-langs';
    lang.setAttribute('list', langList);
    if (!document.getElementById(langList)) {
      document.body.append(el('datalist', { id: langList }, ...codeLanguages.map(([id, name]) => el('option', { value: id }, name))));
    }
    lang.addEventListener('focus', () => lang.select());
    const options = input(fence.options, 'title="file.py" linenums="1" hl_lines="2 3"');
    const code = textarea(fence.code, 16);
    openPanel(fence.lang === 'mermaid' ? 'Mermaid diagram' : 'Code block', el('div', {},
      field('Language', lang, 'Pick from the list or type any language name. Leave empty for no highlighting.'),
      field('Options', options, 'title, linenums, hl_lines and other fence options'),
      field('Code', code),
    ), [
      button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
    ], live(() => formatFence({ fence: fence.fence, lang: lang.value.trim(), options: options.value.trim(), code: code.value })), ctx.nested);
    return;
  }

  if (tabs) {
    const list = el('div', { class: 'ndcms-tabs-editor' });
    const rows: { title: HTMLInputElement; body: RichArea; selected: boolean }[] = [];
    const addRow = (t: { title: string; body: string; selected: boolean }) => {
      const row = { title: input(t.title, 'Tab title'), body: blockList(t.body, s.page.path), selected: t.selected };
      rows.push(row);
      const box = el('fieldset', { class: 'ndcms-tab' }, el('legend', {}, 'Tab'), field('Title', row.title), field('Content', row.body.root),
        button('Remove tab', 'Remove this tab', () => { rows.splice(rows.indexOf(row), 1); box.remove(); }, 'ndcms-btn--danger'));
      list.append(box);
    };
    tabs.forEach(addRow);
    openPanel('Content tabs', el('div', {}, list, button('+ Add tab', 'Add another tab', () => addRow({ title: 'New tab', body: 'Content.', selected: false }))), [
      button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
    ], live(() => formatTabs(rows.map((r) => ({ title: r.title.value, body: r.body.value(), selected: r.selected })))), ctx.nested);
    return;
  }

  const link = !forceSource ? parseLinkBlock(ctx.source) : null;
  if (link) {
    openLinkEditor(ctx, link);
    return;
  }

  const source = textarea(ctx.source, 18);
  openPanel(`${label(ctx.kind)} · markdown`, el('div', {},
    field('Markdown', source, 'Any Netdocs markdown works here: admonitions, tabs, attr lists, icons, macros… Changes show on the page as you type.'),
  ), [], live(() => source.value), ctx.nested);
}

// --- Callout types -------------------------------------------------------------------------------

/** Material's colors for each callout type. */
const admonitionColors: Record<string, string> = {
  note: '#448aff', abstract: '#00b0ff', info: '#00b8d4', tip: '#00bfa5', success: '#00c853', question: '#64dd17',
  warning: '#ff9100', failure: '#ff5252', danger: '#ff1744', bug: '#f50057', example: '#7c4dff', quote: '#9e9e9e',
};

/** A grid of callout types, each in its own color with the icon the theme draws for it. */
function admonitionTypePicker(value: string) {
  let current = value;
  const root = el('div', { class: 'ndcms-types' });
  const custom = input(admonitionTypes.includes(value) ? '' : value, 'or another type…');
  custom.classList.add('ndcms-types__custom');
  const draw = () => {
    root.querySelectorAll('.ndcms-type').forEach((b) => b.classList.toggle('on', (b as HTMLElement).dataset.type === current));
  };
  for (const t of admonitionTypes) {
    const icon = el('span', { class: 'ndcms-type__icon' });
    icon.style.setProperty('--icon', `var(--md-admonition-icon--${t})`);
    const b = el('button', { type: 'button', class: 'ndcms-type', 'data-type': t, title: t }, icon, el('span', {}, t));
    b.style.setProperty('--type-color', admonitionColors[t]);
    b.addEventListener('click', () => { current = t; custom.value = ''; draw(); root.dispatchEvent(new Event('change', { bubbles: true })); });
    root.append(b);
  }
  custom.addEventListener('input', () => { if (custom.value.trim()) { current = custom.value.trim(); draw(); } });
  root.append(custom);
  draw();
  return { root, value: () => current };
}

// --- Mermaid diagrams -----------------------------------------------------------------------------

/** A diagram's front matter: `title`, and the `config` keys this form edits; other lines are kept. */
function parseMermaid(code: string) {
  const m = /^---\n([\s\S]*?)\n---\n?/.exec(code);
  const result = { title: '', theme: '', look: '', other: [] as string[], configOther: [] as string[], body: m ? code.slice(m[0].length) : code };
  if (!m) return result;
  let inConfig = false;
  for (const line of m[1].split('\n')) {
    if (/^config:\s*$/.test(line)) { inConfig = true; continue; }
    if (inConfig && /^\s+/.test(line)) {
      const kv = /^\s+(theme|look):\s*(.*)$/.exec(line);
      if (kv) result[kv[1] as 'theme' | 'look'] = kv[2].replace(/^["']|["']$/g, '');
      else result.configOther.push(line);
      continue;
    }
    inConfig = false;
    const t = /^title:\s*(.*)$/.exec(line);
    if (t) result.title = t[1].replace(/^["']|["']$/g, '');
    else if (line.trim()) result.other.push(line);
  }
  return result;
}

function formatMermaid(d: ReturnType<typeof parseMermaid>): string {
  const lines: string[] = [];
  if (d.title.trim()) lines.push(`title: ${/[:#'"]/.test(d.title) ? JSON.stringify(d.title.trim()) : d.title.trim()}`);
  lines.push(...d.other);
  const config = [...(d.theme ? [`  theme: ${d.theme}`] : []), ...(d.look ? [`  look: ${d.look}`] : []), ...d.configOther];
  if (config.length) lines.push('config:', ...config);
  return (lines.length ? `---\n${lines.join('\n')}\n---\n` : '') + d.body.replace(/^\n+/, '');
}

function openMermaidEditor(ctx: EditCtx, fence: ReturnType<typeof parseFence> & object) {
  const apply = ctx.apply;
  const d = parseMermaid(fence.code);
  const title = input(d.title, 'Shown above the diagram (optional)');
  const theme = select(d.theme, ['', 'default', 'neutral', 'dark', 'forest', 'base']);
  theme.querySelector('option[value=""]')!.textContent = 'Site default (follows light/dark)';
  const look = select(d.look, ['', 'classic', 'handDrawn']);
  look.querySelector('option[value=""]')!.textContent = 'Classic';
  look.querySelector('option[value="handDrawn"]')!.textContent = 'Hand-drawn';
  const code = textarea(d.body, 16);
  const kinds: [string, string][] = [
    [`flowchart TD
    A([Request comes in]) --> B{Signed in?}
    B -->|No| C[Show sign-in page]
    C --> B
    B -->|Yes| D{Cached?}
    D -->|Yes| E[(Cache)]
    D -->|No| F[Build the page]
    F --> G[(Database)]
    F --> E
    E --> H([Send response])
    subgraph Server
        D
        F
    end`, 'Flowchart'],
    [`sequenceDiagram
    autonumber
    actor User
    participant App
    participant API
    participant DB as Database
    User->>App: Open dashboard
    App->>API: GET /stats
    activate API
    API->>DB: Query last 24h
    DB-->>API: Rows
    alt cache is fresh
        API-->>App: Cached stats
    else cache is stale
        API->>API: Recalculate
        API-->>App: New stats
    end
    deactivate API
    App-->>User: Render charts
    Note over App,API: Refreshes every 60 seconds`, 'Sequence'],
    [`classDiagram
    class Device {
        +string name
        +string ip
        +ping() bool
    }
    class Switch {
        +int ports
        +vlans() list
    }
    class Server {
        +int cores
        +int ramGb
        +reboot()
    }
    class Rack {
        +string location
        +add(Device d)
    }
    Device <|-- Switch
    Device <|-- Server
    Rack "1" o-- "*" Device : holds`, 'Class'],
    [`stateDiagram-v2
    [*] --> Idle
    Idle --> Charging : solar > load
    Idle --> Discharging : load > solar
    Charging --> Full : battery 100%
    Full --> Idle : sun sets
    Discharging --> Low : battery < 20%
    Low --> GridAssist : below 10%
    GridAssist --> Idle : sun rises
    state Charging {
        [*] --> Bulk
        Bulk --> Absorb
        Absorb --> Float
    }`, 'State'],
    [`erDiagram
    CUSTOMER ||--o{ ORDER : places
    ORDER ||--|{ LINE_ITEM : contains
    PRODUCT ||--o{ LINE_ITEM : "appears in"
    CUSTOMER {
        int id PK
        string name
        string email
    }
    ORDER {
        int id PK
        int customer_id FK
        date placed
    }
    PRODUCT {
        int id PK
        string sku
        decimal price
    }`, 'Entity relationship'],
    [`gantt
    title Solar install
    dateFormat YYYY-MM-DD
    excludes weekends
    section Planning
    Site survey        :done,    s1, 2026-01-05, 3d
    Permits            :active,  s2, after s1, 10d
    section Build
    Mount rails        :         b1, after s2, 2d
    Panels             :         b2, after b1, 3d
    Wiring and inverter:crit,    b3, after b2, 4d
    section Finish
    Inspection         :milestone, m1, after b3, 0d
    Turn on            :         f1, after m1, 1d`, 'Gantt'],
    [`pie showData
    title Power use by room
    "Server rack" : 42
    "Kitchen" : 23
    "HVAC" : 18
    "Office" : 11
    "Other" : 6`, 'Pie'],
    [`mindmap
  root((Homelab))
    Network
      Firewall
      Core switch
        10G uplinks
      Wi-Fi
    Compute
      Hypervisors
      Containers
    Storage
      NAS
        ZFS pools
      Backups
    Power
      UPS
      Solar`, 'Mind map'],
    [`timeline
    title Homelab history
    section Early days
        2019 : First NAS
             : Pi-hole
        2020 : Rack and UPS
    section Growth
        2021 : 10G network
        2022 : Hypervisor cluster
    section Today
        2024 : Solar power
        2026 : Rebuilt on SFF PCs`, 'Timeline'],
    [`gitGraph
    commit id: "init"
    branch feature
    checkout feature
    commit id: "add editor"
    commit id: "tests"
    checkout main
    commit id: "hotfix"
    merge feature tag: "v1.0"
    commit id: "docs"`, 'Git graph'],
    [`journey
    title Publishing a page
    section Write
      Draft the text: 4: Author
      Add images: 3: Author
    section Review
      Read it over: 4: Author, Editor
      Fix links: 2: Editor
    section Publish
      Build the site: 5: Netdocs
      Share it: 5: Author`, 'User journey'],
  ];
  const starter = select('', ['', ...kinds.map((k) => k[1])]);
  starter.querySelector('option[value=""]')!.textContent = 'Start from an example…';
  starter.addEventListener('change', () => {
    const k = kinds.find((x) => x[1] === starter.value);
    if (k && (!code.value.trim() || confirm('Replace the diagram with this example?'))) code.value = k[0];
    starter.value = '';
  });
  openPanel('Mermaid diagram', el('div', {},
    field('Title', title),
    el('div', { class: 'ndcms-grid2' }, field('Theme', theme), field('Look', look)),
    field('Diagram', code, 'Mermaid syntax. The page shows your change as you type.'),
    starter,
    el('small', { class: 'ndcms-muted' }, 'Syntax help: ', el('a', { href: 'https://mermaid.js.org/intro/', target: '_blank', rel: 'noreferrer' }, 'mermaid.js.org ↗')),
  ), [
    button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
  ], () => apply(formatFence({ ...fence, code: formatMermaid({ ...d, title: title.value, theme: theme.value, look: look.value, body: code.value }) })), ctx.nested);
}

// --- Timelines (timeline plugin) ---------------------------------------------------------------

/** Which days count: `all` (the plugin's default) or `weekdays`. A missing type stays missing unless changed. */
function typeSelect(value: string | undefined) {
  const s = select(value ?? 'all', ['all', 'weekdays']);
  s.querySelector('option[value="all"]')!.textContent = 'Every day';
  s.querySelector('option[value="weekdays"]')!.textContent = 'Weekdays only';
  return { root: s, value: () => (s.value === 'all' && value === undefined ? undefined : s.value) };
}

/** A date typed in the block's `date_format`, with a date picker next to it when that format allows one. */
function timelineDate(value: string, format: () => string, placeholder: string) {
  const text = input(value, placeholder);
  const picker = el('input', { type: 'date', class: 'ndcms-input ndcms-tl__picker', title: 'Pick a date' }) as HTMLInputElement;
  const sync = () => {
    const f = format();
    picker.hidden = !pickerFormat(f);
    picker.value = toIsoDate(text.value, f);
  };
  picker.addEventListener('change', () => {
    if (!picker.value) return;
    text.value = fromIsoDate(picker.value, format());
    text.dispatchEvent(new Event('input', { bubbles: true }));
  });
  text.addEventListener('input', sync);
  sync();
  return { root: el('div', { class: 'ndcms-row' }, text, picker), value: () => text.value.trim(), sync };
}

/** A list of fieldsets with add, remove and move buttons. */
function rowList<T>(legend: string, items: T[], build: (item: T, box: HTMLElement) => () => T, blank: () => T, addLabel: string) {
  const list = el('div', { class: 'ndcms-tl__list' });
  const rows: { box: HTMLElement; value: () => T }[] = [];
  const add = (item: T) => {
    const box = el('fieldset', { class: 'ndcms-tab' }, el('legend', {}, legend));
    const row = { box, value: build(item, box) };
    rows.push(row);
    box.append(el('div', { class: 'ndcms-row' },
      button('↑', 'Move up', () => { const i = rows.indexOf(row); if (i > 0) { rows.splice(i, 1); rows.splice(i - 1, 0, row); list.insertBefore(box, rows[i].box); } }, 'ndcms-btn--small'),
      button('↓', 'Move down', () => { const i = rows.indexOf(row); if (i < rows.length - 1) { rows.splice(i, 1); rows.splice(i + 1, 0, row); rows[i].box.after(box); } }, 'ndcms-btn--small'),
      button('Remove', `Remove this ${legend.toLowerCase()}`, () => { rows.splice(rows.indexOf(row), 1); box.remove(); }, 'ndcms-btn--danger ndcms-btn--small'),
    ));
    list.append(box);
  };
  items.forEach(add);
  const root = el('div', {}, list, button(addLabel, addLabel, () => add(blank())));
  return { root, value: () => rows.map((r) => r.value()) };
}

function openTimelineEditor(ctx: EditCtx, fence: ReturnType<typeof parseFence> & object, t: Timeline) {
  const title = input(t.title ?? '', 'Diagram title (optional)');
  const dateFormat = input(t.date_format ?? '', 'MM/DD/YYYY');
  const display = input(t.display_date_format ?? '', 'dddd MMM dd, yyyy');
  const fmt = () => dateFormat.value.trim() || 'MM/DD/YYYY';
  const summary = el('input', { type: 'checkbox' }) as HTMLInputElement;
  summary.checked = t.show_summary ?? true;
  const editEx = el('input', { type: 'checkbox' }) as HTMLInputElement;
  editEx.checked = t.edit_exclusions ?? false;
  const dates: ReturnType<typeof timelineDate>[] = [];
  const date = (v: string, ph = fmt()) => { const d = timelineDate(v, fmt, ph); dates.push(d); return d; };
  dateFormat.addEventListener('input', () => dates.forEach((d) => d.sync()));

  // Names an `expr` can point at, for the autocomplete list.
  const refs = el('datalist', { id: `ndcms-tl-refs-${Date.now()}` });
  const nameInputs: HTMLInputElement[] = [];
  const refreshRefs = () => refs.replaceChildren(...nameInputs.filter((n) => n.isConnected && n.value.trim()).map((n) => el('option', { value: n.value.trim() })));
  const nameInput = (v: string) => {
    const n = input(v, 'name (letters, digits, _)');
    n.pattern = '[A-Za-z_][A-Za-z0-9_]*';
    nameInputs.push(n);
    n.addEventListener('input', refreshRefs);
    return n;
  };
  const duration = (v: string | undefined) => {
    const d = el('input', { type: 'number', min: '0', step: '1', class: 'ndcms-input', placeholder: '0' }) as HTMLInputElement;
    d.value = v ?? '';
    return d;
  };

  const inputs = rowList<TimelineInput>('Input date', t.inputs, (i, box) => {
    const name = nameInput(i.name);
    const label = input(i.label ?? '', 'Shown on the diagram (defaults to the name)');
    const def = date(i.default);
    const type = typeSelect(i.type);
    const dur = duration(i.duration);
    const editable = el('input', { type: 'checkbox' }) as HTMLInputElement;
    editable.checked = i.editable ?? true;
    box.append(
      el('div', { class: 'ndcms-grid2' }, field('Name', name), field('Label', label)),
      field('Date', def.root, 'Starting value of the date picker on the page.'),
      el('div', { class: 'ndcms-grid2' }, field('Counts', type.root), field('Duration (days)', dur, '0 is a milestone.')),
      el('label', { class: 'ndcms-check' }, editable, ' Readers can change this date'),
    );
    return () => ({
      name: name.value.trim(), label: label.value.trim() || undefined, default: def.value(), type: type.value(),
      duration: dur.value.trim() || undefined, editable: editable.checked ? (i.editable === undefined ? undefined : true) : false,
    });
  }, () => ({ name: 'start', default: fromIsoDate(new Date().toISOString().slice(0, 10), fmt()) }), '+ Add input date');

  const outputs = rowList<TimelineOutput>('Computed date', t.outputs, (o, box) => {
    const name = nameInput(o.name);
    const label = input(o.label ?? '', 'Shown on the diagram (defaults to the name)');
    const parsed = parseExpr(o.expr);
    const exprText = input(o.expr, 'start + 5');
    const ref = input(parsed?.ref ?? '', 'earlier name');
    ref.setAttribute('list', refs.id);
    ref.addEventListener('focus', refreshRefs);
    const sign = select(parsed?.sign ?? '+', ['+', '-']);
    sign.querySelector('option[value="+"]')!.textContent = '+ after';
    sign.querySelector('option[value="-"]')!.textContent = '− before';
    const count = el('input', { type: 'number', min: '0', step: '1', class: 'ndcms-input', placeholder: '0' }) as HTMLInputElement;
    count.value = parsed?.count ?? '';
    // An expr the form can't split stays as text.
    const simple = el('div', { class: 'ndcms-row' }, ref, sign, count, el('span', { class: 'ndcms-muted' }, 'days'));
    simple.hidden = !parsed;
    exprText.hidden = !!parsed;
    const type = typeSelect(o.type);
    const dur = duration(o.duration);
    box.append(
      el('div', { class: 'ndcms-grid2' }, field('Name', name), field('Label', label)),
      field('Starts', parsed ? simple : exprText, 'Counted from the end of the earlier date. Days are counted the way "Counts" says.'),
      el('div', { class: 'ndcms-grid2' }, field('Counts', type.root), field('Duration (days)', dur, '0 is a milestone.')),
    );
    return () => ({
      name: name.value.trim(), label: label.value.trim() || undefined, type: type.value(),
      expr: parsed ? formatExpr(ref.value.trim(), sign.value, count.value) : exprText.value.trim(), duration: dur.value.trim() || undefined,
    });
  }, () => ({ name: `step${nameInputs.filter((n) => n.isConnected).length}`, type: 'weekdays', expr: nameInputs.filter((n) => n.isConnected).at(-1)?.value.trim() + ' + 1', duration: '1' }), '+ Add computed date');

  const exclusions = rowList<TimelineExclusion>('Excluded date', t.exclusions ?? [], (e, box) => {
    const range = el('input', { type: 'checkbox' }) as HTMLInputElement;
    range.checked = typeof e !== 'string';
    const from = date(typeof e === 'string' ? e : e.from);
    const to = date(typeof e === 'string' ? '' : e.to);
    const toField = field('To', to.root);
    toField.hidden = !range.checked;
    range.addEventListener('change', () => { toField.hidden = !range.checked; });
    box.append(field(range.checked ? 'From' : 'Date', from.root), el('label', { class: 'ndcms-check' }, range, ' A range of days'), toField);
    return () => (range.checked && to.value() ? { from: from.value(), to: to.value() } : from.value());
  }, () => '', '+ Add excluded date');

  openPanel('Timeline', el('div', {},
    el('p', { class: 'ndcms-muted' }, 'Input dates get a date picker on the page; computed dates follow from them. ',
      el('a', { href: `${cms.admin}#/plugins?name=timeline`, target: embedded ? '_top' : '_self' }, 'Plugin settings'), ' · ',
      el('a', { href: 'https://xtremeownage.github.io/Netdocs/plugins/timeline/', target: '_blank', rel: 'noreferrer' }, 'Timeline docs ↗')),
    refs,
    field('Title', title),
    el('div', { class: 'ndcms-grid2' },
      field('Date format', dateFormat, 'How the dates below are written. Tokens: YYYY MM DD.'),
      field('Table date format', display, 'How the summary table shows dates.')),
    el('div', { class: 'ndcms-checks' },
      el('label', { class: 'ndcms-check' }, summary, ' Show the date table'),
      el('label', { class: 'ndcms-check' }, editEx, ' Readers can add and remove excluded dates')),
    field('Input dates', inputs.root),
    field('Computed dates', outputs.root, 'Each one can only use names listed above it.'),
    field('Excluded dates', exclusions.root, 'Holidays and closures. They never count as a day.'),
  ), [
    button('Edit source', 'Edit the raw YAML', () => ctx.toSource()),
  ], () => ctx.apply(formatFence({
    ...fence,
    code: formatTimeline({
      title: title.value.trim() || undefined,
      date_format: dateFormat.value.trim() || undefined,
      display_date_format: display.value.trim() || undefined,
      show_summary: summary.checked ? (t.show_summary === undefined ? undefined : true) : false,
      edit_exclusions: editEx.checked ? true : (t.edit_exclusions === undefined ? undefined : false),
      exclusions: exclusions.value().filter((e) => (typeof e === 'string' ? e : e.from)),
      inputs: inputs.value(),
      outputs: outputs.value(),
    }),
  })), ctx.nested);
  refreshRefs();
}

// --- Image blocks --------------------------------------------------------------------------------

interface ImageBlock {
  src: string; alt: string; title: string; width: string; align: string; attrs: string;
  caption: string; href: string; linkTitle: string; linkAttrs: string;
}

const IMG = String.raw`!\[([^\]]*)\]\(\s*<?([^\s)>]+)>?(?:\s+"([^"]*)")?\s*\)(\{[^}]*\})?`;

/** Splits `{ width="400" align=left .round }` into width, align and the rest. */
function imageAttrs(raw: string | undefined) {
  const inner = (raw ?? '').replace(/^\{|\}$/g, '').trim();
  const parts = inner.match(/(?:[^\s"']+(?:="[^"]*"|='[^']*')?)+/g) ?? [];
  const take = (k: string) => {
    const i = parts.findIndex((p) => p.startsWith(k + '='));
    if (i < 0) return '';
    const v = parts.splice(i, 1)[0].slice(k.length + 1).replace(/^["']|["']$/g, '');
    return v;
  };
  const width = take('width');
  const align = take('align');
  return { width, align, rest: parts.join(' ') };
}

/** An image block: a plain image, an image that links somewhere, either one in a figure with a caption. */
function parseImageBlock(source: string): ImageBlock | null {
  let s = source.trim();
  let caption = '';
  const fig = /^<figure markdown(?:="1")?>\s*\n([\s\S]*?)\n\s*<figcaption>([\s\S]*?)<\/figcaption>\s*\n<\/figure>$/.exec(s);
  const caret = fig ? null : /^\^{3,}\s*\n([\s\S]*?)\n\^{3,}[ \t]*(.*)$/.exec(s);
  if (fig) { s = fig[1].trim(); caption = fig[2].replace(/&lt;/g, '<').trim(); }
  else if (caret) { s = caret[1].trim(); caption = caret[2].trim(); }
  const linked = new RegExp(String.raw`^\[(${IMG})\]\(\s*<?([^\s)>]+)>?(?:\s+"([^"]*)")?\s*\)(\{[^}]*\})?$`).exec(s);
  const plain = linked ? null : new RegExp('^' + IMG + '$').exec(s);
  const m = linked ? new RegExp('^' + IMG + '$').exec(linked[1]) : plain;
  if (!m) return null;
  const a = imageAttrs(m[4]);
  return {
    src: m[2], alt: m[1], title: m[3] ?? '', width: a.width, align: a.align, attrs: a.rest, caption,
    href: linked ? linked[6] : '', linkTitle: linked ? linked[7] ?? '' : '', linkAttrs: linked ? (linked[8] ?? '').replace(/^\{|\}$/g, '').trim() : '',
  };
}

function formatImageBlock(i: ImageBlock): string {
  const attrs = [i.width ? `width="${i.width.replace(/[^\d%a-z]/gi, '')}"` : '', i.align ? `align=${i.align}` : '', i.attrs.trim()].filter(Boolean).join(' ');
  const title = i.title.trim() ? ` "${i.title.trim().replace(/"/g, '&quot;')}"` : '';
  let md = `![${i.alt.replace(/[[\]]/g, '')}](${i.src}${title})${attrs ? `{ ${attrs} }` : ''}`;
  if (i.href.trim()) {
    const lt = i.linkTitle.trim() ? ` "${i.linkTitle.trim().replace(/"/g, '&quot;')}"` : '';
    md = `[${md}](${i.href.trim()}${lt})${i.linkAttrs.trim() ? `{${i.linkAttrs.trim()}}` : ''}`;
  }
  if (!i.caption.trim()) return md;
  // Markdig's figure syntax: the caption follows the closing ^^^.
  return `^^^\n${md}\n^^^ ${i.caption.trim().replace(/\n/g, ' ')}`;
}

function openImageEditor(ctx: EditCtx, img: ImageBlock) {
  const apply = ctx.apply;
  if (!state) return;
  const s = state;
  const cur = { ...img };
  const preview = el('img', { class: 'ndcms-image-editor__preview', alt: '' }) as HTMLImageElement;
  const srcLabel = el('span', { class: 'ndcms-muted ndcms-image-editor__src' });
  const draw = () => {
    preview.src = /^(https?:)?\/\//.test(cur.src) ? cur.src : api.mediaUrl(docsPath(s.page.path, decodeURI(cur.src)));
    srcLabel.textContent = decodeURI(cur.src);
  };
  draw();
  const change = button('Change image…', 'Pick another image or upload one', async () => {
    const picked = await openImagePicker(s.page.path, { alt: alt.value, path: docsPath(s.page.path, decodeURI(cur.src)) });
    if (!picked) return;
    cur.src = encodeURI(picked.src);
    if (!alt.value && picked.alt) alt.value = picked.alt;
    draw();
    preview.dispatchEvent(new Event('change', { bubbles: true }));
  });
  const alt = input(img.alt, 'Describe the image for screen readers and search');
  const title = input(img.title, 'Shown when hovering (optional)');
  const width = input(img.width, 'e.g. 400 or 50%');
  const align = select(img.align, ['', 'left', 'right']);
  align.querySelector('option[value=""]')!.textContent = 'In line with the text';
  align.querySelector('option[value="left"]')!.textContent = 'Float left, text wraps';
  align.querySelector('option[value="right"]')!.textContent = 'Float right, text wraps';
  const caption = input(img.caption, 'Shown under the image (optional)');
  const href = input(img.href, 'Empty: the image is not a link');
  const browse = button('Browse…', 'Link to a page, a file or a URL', async () => {
    const chosen = await openLinkPicker(s.page.path, href.value);
    if (chosen) { href.value = chosen; href.dispatchEvent(new Event('input', { bubbles: true })); }
  });
  const linkParts = attrParts(img.linkAttrs);
  const newTab = el('input', { type: 'checkbox' }) as HTMLInputElement;
  newTab.checked = linkParts.target === '_blank';
  const attrs = input(img.attrs, '.shadow loading=lazy');
  const compute = () => formatImageBlock({
    ...cur, alt: alt.value, title: title.value, width: width.value.trim(), align: align.value, caption: caption.value,
    href: href.value, linkTitle: img.linkTitle, attrs: attrs.value,
    linkAttrs: [newTab.checked ? 'target=_blank' : '', linkParts.rest].filter(Boolean).join(' '),
  });
  openPanel('Image', el('div', {},
    el('div', { class: 'ndcms-image-editor' }, preview, el('div', {}, srcLabel, change)),
    field('Alt text', alt, 'Read aloud by screen readers. Leave empty only for decorative images.'),
    field('Caption', caption),
    el('div', { class: 'ndcms-grid2' }, field('Width', width, 'Pixels, or a percentage'), field('Placement', align)),
    field('Tooltip', title),
    el('div', { class: 'ndcms-section-title' }, 'Link'),
    field('Opens', el('div', { class: 'ndcms-row' }, href, browse), 'Make the image a link to a page, a full-size copy, or another site'),
    el('label', { class: 'ndcms-check' }, newTab, ' Open in a new tab'),
    field('Other attributes', attrs, 'Attribute list for the image: classes (.shadow) and key=value pairs'),
  ), [
    button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
  ], () => apply(compute()), ctx.nested);
}

// --- Link blocks ---------------------------------------------------------------------------------

interface LinkBlock { text: string; url: string; title: string; attrs: string; footnotes: string[] }

/** A block that is one link: `[text](url "title"){attrs}[^note]`. The text may be an image. */
function parseLinkBlock(source: string): LinkBlock | null {
  const m = /^\[((?:!\[[^\]]*\]\([^)]*\)|[^\]])*)\]\(\s*<?([^\s)>]+)>?(?:\s+"([^"]*)")?\s*\)(\{[^}]*\})?((?:\[\^[^\]]+\])*)\s*$/.exec(source.trim());
  if (!m) return null;
  return {
    text: m[1], url: m[2], title: m[3] ?? '',
    attrs: m[4] ? m[4].slice(1, -1).trim() : '',
    footnotes: [...(m[5] ?? '').matchAll(/\[\^([^\]]+)\]/g)].map((x) => x[1]),
  };
}

function formatLinkBlock(l: LinkBlock): string {
  const title = l.title.trim() ? ` "${l.title.trim().replace(/"/g, '&quot;')}"` : '';
  const attrs = l.attrs.trim() ? `{${l.attrs.trim()}}` : '';
  return `[${l.text}](${l.url.trim()}${title})${attrs}${l.footnotes.map((f) => `[^${f}]`).join('')}`;
}

/** Splits an attr list into `target=_blank` and the rest. */
const attrParts = (attrs: string) => {
  const parts = attrs.match(/(?:[^\s"']+(?:="[^"]*"|='[^']*')?)+/g) ?? [];
  const target = parts.find((p) => /^target=/.test(p));
  return { target: target ? target.replace(/^target=["']?|["']$/g, '') : '', rest: parts.filter((p) => p !== target).join(' ') };
};

function openLinkEditor(ctx: EditCtx, link: LinkBlock) {
  const apply = ctx.apply;
  if (!state) return;
  const s = state;
  const image = parseImageMarkdown(link.text, s.page.path);
  let picked: { src: string; alt: string } | null = image ? { src: image.src, alt: image.alt } : null;
  const kind = select(picked ? 'image' : 'text', ['text', 'image']);
  kind.querySelector('option[value="text"]')!.textContent = 'Text';
  kind.querySelector('option[value="image"]')!.textContent = 'Image';
  const text = input(picked ? '' : link.text, 'What the reader clicks');
  const imagePreview = el('div', { class: 'ndcms-link__image' });
  const drawImage = () => {
    imagePreview.replaceChildren();
    if (picked) {
      const img = el('img', { alt: picked.alt }) as HTMLImageElement;
      img.src = picked.src.startsWith('http') ? picked.src : new URL(picked.src, location.href).href;
      imagePreview.append(img, el('span', { class: 'ndcms-muted' }, picked.src));
    }
  };
  drawImage();
  const chooseImage = button('Choose image…', 'Pick the image to show as the link', async () => {
    const p = await openImagePicker(s.page.path, image ?? {});
    if (!p) return;
    picked = { src: p.src, alt: p.alt };
    drawImage();
    imagePreview.dispatchEvent(new Event('change', { bubbles: true }));
  });
  const textField = field('Link text', text);
  const imageField = el('div', { class: 'ndcms-field' }, el('span', { class: 'ndcms-field__label' }, 'Image'), imagePreview, chooseImage);
  const showKind = () => { textField.style.display = kind.value === 'text' ? '' : 'none'; imageField.style.display = kind.value === 'image' ? '' : 'none'; };
  kind.addEventListener('change', showKind);
  showKind();

  const url = input(link.url, 'https://… or a page such as ../setup.md');
  const browse = button('Browse…', 'Pick a page or paste a URL', async () => {
    const chosen = await openLinkPicker(s.page.path, url.value);
    if (chosen) { url.value = chosen; url.dispatchEvent(new Event('input', { bubbles: true })); }
  });
  const title = input(link.title, 'Shown when hovering over the link');
  const parts = attrParts(link.attrs);
  const newTab = el('input', { type: 'checkbox' }) as HTMLInputElement;
  newTab.checked = parts.target === '_blank';
  const otherAttrs = input(parts.rest, '.button rel=nofollow');
  // Footnotes defined on this page, for suggestions.
  const defined = [...new Set(s.blocks.flatMap((b) => [...b.source.matchAll(/^\[\^([^\]]+)\]:/gm)].map((m) => m[1])))];
  const notes = chipInput(link.footnotes, defined);
  const missing = el('small', { class: 'ndcms-muted' });
  const checkNotes = () => {
    const absent = notes.values().filter((n) => !defined.includes(n));
    missing.textContent = absent.length ? `Not defined on this page: ${absent.join(', ')}. Add a footnote block with [^${absent[0]}]: text.` : (defined.length ? 'Defined on this page: ' + defined.join(', ') : '');
  };
  checkNotes();

  const compute = () => {
    checkNotes();
    const attrs = [newTab.checked ? 'target=_blank' : '', otherAttrs.value.trim()].filter(Boolean).join(' ');
    const linkText = kind.value === 'image' && picked ? `![${picked.alt}](${picked.src})` : text.value || url.value;
    return formatLinkBlock({ text: linkText, url: url.value || '#', title: title.value, attrs, footnotes: notes.values() });
  };
  openPanel('Link', el('div', {},
    field('Show as', kind),
    textField, imageField,
    field('Address', el('div', { class: 'ndcms-row' }, url, browse)),
    field('Tooltip', title, 'Optional title attribute'),
    el('label', { class: 'ndcms-check' }, newTab, ' Open in a new tab (target=_blank)'),
    field('Other attributes', otherAttrs, 'Attribute list: classes (.button .button--primary), ids (#id) and key=value pairs'),
    field('Footnotes', notes.root, 'Footnote markers after the link, such as an affiliate disclosure'),
    missing,
  ), [
    button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
  ], () => apply(compute()), ctx.nested);
}

// --- Blocks inside callouts, tabs, cards and grids -----------------------------------------------

interface RichArea { root: HTMLElement; value: () => string }

interface NestedItem { source: string; kind: string; html: string; rich: boolean; content?: HTMLElement; renderedHtml?: string; seq?: number }

/** Puts the cursor on a new block's placeholder text, so typing replaces it. */
function selectPlaceholder(content: HTMLElement) {
  content.focus();
  const target = content.querySelector('li, h1, h2, h3, h4, h5, h6, p, blockquote') ?? content;
  const range = document.createRange();
  range.selectNodeContents(target);
  const box = target.querySelector('input[type="checkbox"]');
  if (box) range.setStartAfter(box);
  const sel = window.getSelection();
  sel?.removeAllRanges();
  sel?.addRange(range);
}

/**
 * Edits the markdown inside a callout, tab, card or grid the way the page itself is edited: text
 * is typed in place, any block can be added between the others, and code, tables, images, nested
 * callouts and the rest open their own form on top of this panel. "Edit as markdown" switches the
 * whole content to its source at any time.
 */
function blockList(markdown: string, pagePath: string): RichArea {
  const root = el('div', { class: 'ndcms-nested' });
  const list = el('div', { class: 'ndcms-nested__list md-typeset' });
  const source = textarea(markdown, 10);
  let mode: 'loading' | 'blocks' | 'source' = 'loading';
  let items: NestedItem[] = [];
  const frontMatter = () => state?.frontMatter ?? {};
  const changed = () => root.dispatchEvent(new Event('input', { bubbles: true }));

  const sync = () => {
    for (const it of items) {
      if (!it.rich || !it.content || it.content.innerHTML === it.renderedHtml) continue;
      it.source = htmlToMarkdown(it.content.innerHTML);
      it.renderedHtml = it.content.innerHTML;
    }
  };
  const value = () => {
    if (mode === 'source') return source.value;
    if (mode === 'loading') return markdown;
    sync();
    return items.map((i) => i.source.replace(/\s+$/, '')).filter((x) => x.trim()).join('\n\n');
  };
  const render = async (it: NestedItem) => {
    const { blocks } = await api.renderBlocks(pagePath, it.source, frontMatter());
    it.html = blocks.map((b) => b.html).join('\n');
    it.kind = wrapperKind(it.source) ?? (blocks.length === 1 ? blocks[0].kind : blocks.length ? 'mixed' : 'empty');
    it.rich = blocks.length === 1 && isRich(it.kind, it.source, it.html);
  };

  const edit = (it: NestedItem, forceSource = false) => {
    sync();
    editBlock({
      source: it.source, kind: it.kind, nested: true,
      apply: async (md) => {
        if (md.trim() === it.source.trim()) return;
        it.source = md;
        // Tell the outer panel now, so closing both panels at once still saves this change.
        changed();
        const mine = it.seq = (it.seq ?? 0) + 1;
        try { await render(it); } catch (e) { alertError(e); return; }
        if (mine === it.seq && items.includes(it)) draw();
      },
      toSource: () => { closePanel(); edit(it, true); },
    }, forceSource);
  };

  const add = async (at: number, id: string) => {
    const t = insertTemplates.find((x) => x.id === id);
    if (!t) return;
    let md = t.markdown;
    const isImage = id === 'image' || id === 'figure';
    if (isImage) {
      const picked = await openImagePicker(pagePath);
      if (!picked) return;
      md = imageMarkdown(picked);
    }
    const it: NestedItem = { source: md, kind: 'new', html: '', rich: false };
    try { await render(it); } catch (e) { alertError(e); return; }
    sync();
    items.splice(at, 0, it);
    draw();
    changed();
    if (it.rich && it.content) selectPlaceholder(it.content);
    else if (!isImage) edit(it);
  };

  const move = (i: number, delta: number) => {
    const j = i + delta;
    if (j < 0 || j >= items.length) return;
    sync();
    [items[i], items[j]] = [items[j], items[i]];
    draw();
    changed();
  };

  const adder = (at: number, last = false) => {
    const wrap = el('div', { class: `ndcms-insert ndcms-nested__insert${last ? ' ndcms-nested__insert--end' : ''}` });
    const close = () => wrap.classList.remove('ndcms-insert--open');
    const menu = blockMenu(() => at, close, (id) => void add(at, id));
    const toggle = button(last ? '+ Add a block' : '+', 'Add a block here', () => {
      const open = wrap.classList.toggle('ndcms-insert--open');
      if (open) menu.querySelector<HTMLInputElement>('input')?.focus();
    }, last ? 'ndcms-nested__add' : 'ndcms-insert__toggle');
    wrap.append(...(last ? [] : [el('span', { class: 'ndcms-insert__line' })]), toggle, menu);
    return wrap;
  };

  const itemEl = (it: NestedItem, i: number) => {
    const content = el('div', { class: 'ndcms-nested__content' });
    content.innerHTML = it.html || `<p class="ndcms-muted">(${label(it.kind)} with no visible output)</p>`;
    content.querySelectorAll('a, button, input, label, summary').forEach((n) => n.setAttribute('tabindex', '-1'));
    it.content = content;
    const tools = el('div', { class: 'ndcms-nested__tools' }, el('span', { class: 'ndcms-block__kind' }, label(it.kind)));
    if (!it.rich) tools.append(button('✎', 'Edit this block', () => edit(it)));
    tools.append(
      button('&lt;/&gt;', 'Edit the markdown source', () => edit(it, true)),
      button('↑', 'Move up', () => move(i, -1)),
      button('↓', 'Move down', () => move(i, 1)),
      button('🗑', 'Remove this block', () => { sync(); items.splice(i, 1); draw(); changed(); }, 'ndcms-btn--danger'),
    );
    const wrap = el('div', { class: `ndcms-nested__item${it.rich ? '' : ' ndcms-nested__item--structured'}` }, tools, content);
    if (it.rich) {
      content.contentEditable = 'true';
      content.spellcheck = true;
      content.addEventListener('focus', () => showFormatBar(content, wrap));
      listKeys(content);
      content.addEventListener('click', (e) => { if ((e.target as HTMLElement).closest('a')) e.preventDefault(); });
    } else {
      wrap.addEventListener('click', (e) => {
        if ((e.target as HTMLElement).closest('.ndcms-nested__tools')) return;
        e.preventDefault();
        edit(it);
      });
    }
    it.renderedHtml = content.innerHTML;
    return wrap;
  };

  const toggle = button('Edit as markdown', 'Switch between visual and markdown editing', () => {
    if (mode === 'blocks') { source.value = value(); showSource(); } else if (mode === 'source') void load(source.value);
  }, 'ndcms-btn--link');

  function draw() {
    sync();
    list.replaceChildren();
    items.forEach((it, i) => list.append(adder(i), itemEl(it, i)));
    list.append(adder(items.length, true));
    refreshThemeWidgets(list);
  }
  function showSource() {
    mode = 'source';
    toggle.textContent = 'Edit visually';
    root.replaceChildren(source, toggle);
  }
  async function load(md: string) {
    try {
      const { blocks } = await api.renderBlocks(pagePath, md, frontMatter());
      items = mergeWrappers(blocks).map((b) => ({ source: b.source, kind: b.kind, html: b.html, rich: isRich(b.kind, b.source, b.html) }));
      mode = 'blocks';
      toggle.textContent = 'Edit as markdown';
      root.replaceChildren(list, toggle);
      draw();
    } catch {
      showSource();
    }
  }
  root.append(el('span', { class: 'ndcms-muted' }, 'Loading…'));
  void load(markdown);
  return { root, value };
}

// --- Grids and card grids ------------------------------------------------------------------------

interface Card {
  icon: string; large: boolean; iconAttrs: string; title: string; strong: string; rule: boolean;
  body: string; linkIcon: string; linkText: string; url: string;
}

/** Turns `material-clock-fast` into the picker's `material/clock-fast`. */
const iconFromShortcode = (code: string) =>
  code.startsWith('fontawesome-') ? code.replace(/^fontawesome-([^-]+)-/, 'fontawesome/$1/') : code.replace('-', '/');
const shortcodeFromIcon = (name: string) => `:${name.replace(/\//g, '-')}:`;

/** `<div class="grid cards" markdown>` with a list where each item is one card. */
function parseCardGrid(source: string): { open: string; cards: Card[] } | null {
  const m = /^(<div\s+class="grid cards"[^>]*>)[ \t]*\n([\s\S]*?)\n?<\/div>\s*$/.exec(source.trim());
  if (!m) return null;
  const raw: string[][] = [];
  let width = 4;
  for (const line of m[2].split('\n')) {
    const start = /^[-*+]( +)(.*)$/.exec(line);
    if (start) { width = 1 + start[1].length; raw.push([start[2]]); continue; }
    if (!raw.length) { if (line.trim()) return null; continue; }
    if (line.trim() && !/^\s/.test(line)) return null;
    raw[raw.length - 1].push(line.replace(new RegExp(`^ {1,${width}}`), ''));
  }
  return { open: m[1], cards: raw.map(parseCard) };
}

function parseCard(lines: string[]): Card {
  const head = /^(?::([\w-]+):)?(\{[^}]*\})?\s*(.*)$/.exec(lines[0].trim())!;
  const t = /^(\*\*|__)(.*)\1$/.exec(head[3].trim());
  let rest = lines.slice(1).join('\n').replace(/^\s*\n/, '');
  let rule = false;
  if (/^---\s*(\n|$)/.test(rest)) { rule = true; rest = rest.replace(/^---\s*\n?/, ''); }
  const paras = rest.trim().split(/\n\s*\n/);
  const link = /^\[(?::([\w-]+):\s*)?([^\]]*)\]\(\s*([^)\s]+)\s*\)$/.exec(paras[paras.length - 1]?.trim() ?? '');
  if (link) paras.pop();
  const attrs = head[2] ?? '';
  return {
    icon: head[1] ? iconFromShortcode(head[1]) : '',
    large: /\.lg\b/.test(attrs), iconAttrs: attrs.replace(/^\{|\}$/g, '').replace(/\.lg\b|\.middle\b/g, '').trim(),
    title: t ? t[2] : head[3].trim(), strong: t ? t[1] : '__', rule,
    body: paras.join('\n\n').trim(),
    linkIcon: link?.[1] ? iconFromShortcode(link[1]) : '', linkText: link?.[2] ?? '', url: link?.[3] ?? '',
  };
}

function formatCard(c: Card): string {
  const attrs = [c.large ? '.lg .middle' : '', c.iconAttrs].filter(Boolean).join(' ');
  const icon = c.icon ? shortcodeFromIcon(c.icon) + (attrs ? `{ ${attrs} }` : '') + ' ' : '';
  const title = c.title.trim() ? `${c.strong}${c.title.trim()}${c.strong}` : '';
  const parts: string[] = [];
  if (c.rule) parts.push('---');
  if (c.body.trim()) parts.push(c.body.trim());
  if (c.url.trim()) parts.push(`[${c.linkIcon ? shortcodeFromIcon(c.linkIcon) + ' ' : ''}${c.linkText.trim() || c.url.trim()}](${c.url.trim()})`);
  const indent = (s: string) => s.split('\n').map((l) => (l.trim() ? '    ' + l : '')).join('\n');
  return `-   ${(icon + title).trim()}` + (parts.length ? '\n\n' + parts.map(indent).join('\n\n') : '');
}

function formatCardGrid(open: string, cards: Card[]): string {
  return `${open}\n\n${cards.map(formatCard).join('\n\n')}\n\n</div>`;
}

/** A plain `<div class="grid" markdown>`: each block inside is one cell. */
function parsePlainGrid(source: string): { open: string; body: string } | null {
  const m = /^(<div\s+class="grid"[^>]*>)[ \t]*\n([\s\S]*?)\n?<\/div>\s*$/.exec(source.trim());
  return m ? { open: m[1], body: m[2].trim() } : null;
}

function openCardGridEditor(ctx: EditCtx, grid: { open: string; cards: Card[] }) {
  if (!state) return;
  const s = state;
  const list = el('div', { class: 'ndcms-cards-editor' });
  type Row = { card: Card; icon: ReturnType<typeof iconInput>; large: HTMLInputElement; title: HTMLInputElement; rule: HTMLInputElement; body: RichArea; linkIcon: ReturnType<typeof iconInput>; linkText: HTMLInputElement; url: HTMLInputElement; box: HTMLElement };
  const rows: Row[] = [];
  const check = (on: boolean) => { const c = el('input', { type: 'checkbox' }) as HTMLInputElement; c.checked = on; return c; };
  const redraw = () => {
    rows.forEach((r, i) => { r.box.querySelector('legend')!.textContent = `Card ${i + 1}`; });
    list.replaceChildren(...rows.map((r) => r.box));
  };
  const addRow = (card: Card, at = rows.length) => {
    const row = {
      card, icon: iconInput(card.icon), large: check(card.large), title: input(card.title, 'Card title'), rule: check(card.rule),
      body: blockList(card.body, s.page.path), linkIcon: iconInput(card.linkIcon), linkText: input(card.linkText, 'e.g. Read more'),
      url: input(card.url, 'Empty: no link'),
    } as Row;
    const browse = button('Browse…', 'Link to a page, a file or a URL', async () => {
      const chosen = await openLinkPicker(s.page.path, row.url.value);
      if (chosen) { row.url.value = chosen; row.url.dispatchEvent(new Event('input', { bubbles: true })); }
    });
    const shift = (d: number) => {
      const i = rows.indexOf(row), j = i + d;
      if (j < 0 || j >= rows.length) return;
      [rows[i], rows[j]] = [rows[j], rows[i]];
      redraw();
    };
    row.box = el('fieldset', { class: 'ndcms-tab ndcms-card' },
      el('legend', {}, 'Card'),
      el('div', { class: 'ndcms-card__tools' },
        button('↑', 'Move this card up', () => shift(-1)),
        button('↓', 'Move this card down', () => shift(1)),
        button('⧉', 'Duplicate this card', () => addRow({ ...currentCard(row) }, rows.indexOf(row) + 1)),
        button('🗑', 'Remove this card', () => { rows.splice(rows.indexOf(row), 1); redraw(); }, 'ndcms-btn--danger')),
      el('div', { class: 'ndcms-grid2' }, field('Icon', row.icon.root), el('label', { class: 'ndcms-check ndcms-card__large' }, row.large, ' Large icon')),
      field('Title', row.title),
      el('label', { class: 'ndcms-check' }, row.rule, ' Line under the title'),
      field('Text', row.body.root),
      el('div', { class: 'ndcms-section-title' }, 'Link at the bottom'),
      field('Address', el('div', { class: 'ndcms-row' }, row.url, browse)),
      el('div', { class: 'ndcms-grid2' }, field('Link text', row.linkText), field('Link icon', row.linkIcon.root)),
    );
    rows.splice(at, 0, row);
    redraw();
  };
  const currentCard = (r: Row): Card => ({
    ...r.card, icon: r.icon.value(), large: r.large.checked, title: r.title.value, rule: r.rule.checked,
    body: r.body.value(), linkIcon: r.linkIcon.value(), linkText: r.linkText.value, url: r.url.value,
  });
  grid.cards.forEach((c) => addRow(c));
  const blank: Card = { icon: 'material/star', large: true, iconAttrs: '', title: 'New card', strong: '__', rule: true, body: 'Card text.', linkIcon: 'octicons/arrow-right-24', linkText: '', url: '' };
  openPanel('Card grid', el('div', {},
    el('p', { class: 'ndcms-muted' }, 'Cards sit side by side and wrap to new rows on smaller screens.'),
    list,
    button('+ Add card', 'Add another card', () => addRow({ ...blank })),
  ), [
    button('Edit source', 'Edit the raw markdown', () => ctx.toSource()),
  ], () => ctx.apply(formatCardGrid(grid.open, rows.map(currentCard))), ctx.nested);
}

// --- Tables -------------------------------------------------------------------------------------

type Align = '' | 'left' | 'center' | 'right';
interface Table { header: string[]; align: Align[]; rows: string[][] }

function splitRow(line: string): string[] {
  let t = line.trim();
  if (t.startsWith('|')) t = t.slice(1);
  if (t.endsWith('|') && !t.endsWith('\\|')) t = t.slice(0, -1);
  const cells: string[] = [];
  let cur = '';
  let code = false;
  for (let i = 0; i < t.length; i++) {
    const c = t[i];
    if (c === '\\' && t[i + 1] === '|') { cur += '\\|'; i++; continue; }
    if (c === '`') code = !code;
    if (c === '|' && !code) { cells.push(cur.trim()); cur = ''; continue; }
    cur += c;
  }
  cells.push(cur.trim());
  return cells;
}

/** Parses a pipe table, or null when the source is anything else. */
function parseTable(source: string): Table | null {
  const lines = source.trim().split('\n');
  if (lines.length < 2 || !/^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$/.test(lines[1])) return null;
  const header = splitRow(lines[0]);
  const align = splitRow(lines[1]).map((c): Align => (c.startsWith(':') && c.endsWith(':') ? 'center' : c.endsWith(':') ? 'right' : c.startsWith(':') ? 'left' : ''));
  const width = header.length;
  const fit = (r: string[]) => [...r, ...Array(Math.max(0, width - r.length)).fill('')].slice(0, width);
  return { header, align: fit(align as string[]) as Align[], rows: lines.slice(2).filter((l) => l.trim()).map((l) => fit(splitRow(l))) };
}

function formatTable(t: Table): string {
  const cols = t.header.length;
  const esc = (c: string) => c.replace(/\n/g, ' ').trim();
  const widths = Array.from({ length: cols }, (_, i) => Math.max(3, esc(t.header[i] ?? '').length, ...t.rows.map((r) => esc(r[i] ?? '').length)));
  const line = (cells: string[]) => '| ' + cells.map((c, i) => esc(c).padEnd(widths[i])).join(' | ') + ' |';
  const sep = '| ' + t.align.map((a, i) => {
    const w = widths[i];
    if (a === 'center') return ':' + '-'.repeat(w - 2) + ':';
    if (a === 'right') return '-'.repeat(w - 1) + ':';
    if (a === 'left') return ':' + '-'.repeat(w - 1);
    return '-'.repeat(w);
  }).join(' | ') + ' |';
  return [line(t.header), sep, ...t.rows.map(line)].join('\n');
}

/** A grid of inputs with buttons to add and remove rows and columns. */
function tableEditor(initial: Table) {
  const t: Table = structuredClone(initial);
  const root = el('div', { class: 'ndcms-table-editor' });
  const draw = () => {
    const grid = el('table', { class: 'ndcms-grid' });
    const head = el('tr', {});
    head.append(el('th', {}));
    t.header.forEach((_, c) => {
      const align = select(t.align[c] || 'default', ['default', 'left', 'center', 'right']);
      align.addEventListener('change', () => { t.align[c] = (align.value === 'default' ? '' : align.value) as Align; });
      head.append(el('th', {}, align, button('✕', 'Remove this column', () => {
        if (t.header.length === 1) return;
        t.header.splice(c, 1); t.align.splice(c, 1); t.rows.forEach((r) => r.splice(c, 1)); draw();
      }, 'ndcms-btn--danger ndcms-btn--tiny')));
    });
    grid.append(head);
    const cell = (value: string, onInput: (v: string) => void, headerCell = false) => {
      const i = input(value, headerCell ? 'Heading' : '');
      if (headerCell) i.classList.add('ndcms-input--strong');
      i.addEventListener('input', () => onInput(i.value));
      return el('td', {}, i);
    };
    const hr = el('tr', {}, el('td', { class: 'ndcms-muted' }, 'Header'));
    t.header.forEach((h, c) => hr.append(cell(h, (v) => { t.header[c] = v; }, true)));
    grid.append(hr);
    t.rows.forEach((r, ri) => {
      const tr = el('tr', {}, el('td', {}, button('✕', 'Remove this row', () => { t.rows.splice(ri, 1); draw(); }, 'ndcms-btn--danger ndcms-btn--tiny')));
      r.forEach((v, c) => tr.append(cell(v, (nv) => { t.rows[ri][c] = nv; })));
      grid.append(tr);
    });
    root.replaceChildren(
      el('div', { class: 'ndcms-grid-wrap' }, grid),
      el('div', { class: 'ndcms-row' },
        button('+ Row', 'Add a row at the bottom', () => { t.rows.push(t.header.map(() => '')); draw(); }),
        button('+ Column', 'Add a column on the right', () => { t.header.push('Column'); t.align.push(''); t.rows.forEach((r) => r.push('')); draw(); }),
      ),
      el('small', { class: 'ndcms-muted' }, 'Cells can use inline formatting such as **bold**, `code` and links.'),
    );
  };
  draw();
  return { root, value: () => t };
}

// Front matter keys with their own controls; anything else is shown as a custom attribute.
const knownKeys = ['title', 'description', 'tags', 'categories', 'date', 'draft', 'icon', 'template', 'hide', 'nav_title', 'page_title', 'authors', 'slug', 'image', 'rss_title', 'rss_description', 'tags_title', 'redirect_from'];

/** The docs-relative path of a page-relative file reference, for thumbnails. */
function docsPath(pagePath: string, ref: string): string {
  const parts = pagePath.split('/').slice(0, -1);
  for (const seg of ref.replace(/^\.\//, '').split('/')) {
    if (seg === '..') parts.pop(); else if (seg && seg !== '.') parts.push(seg);
  }
  return parts.join('/');
}

function imageInput(value: string, pagePath: string) {
  const text = input(value, 'Leave empty to use the first image in the post');
  const thumb = el('img', { class: 'ndcms-thumb', alt: '' }) as HTMLImageElement;
  const draw = () => {
    const v = text.value.trim();
    thumb.hidden = !v;
    if (v) thumb.src = /^(https?:)?\/\//.test(v) ? v : api.mediaUrl(v.startsWith('/') ? v.slice(1) : docsPath(pagePath, v));
  };
  thumb.addEventListener('error', () => { thumb.hidden = true; });
  text.addEventListener('input', draw);
  const choose = button('Choose…', 'Pick an image from the media library', async () => {
    const picked = await openImagePicker(pagePath, {});
    if (!picked) return;
    text.value = picked.src;
    draw();
    text.dispatchEvent(new Event('input', { bubbles: true }));
  });
  draw();
  return { root: el('div', { class: 'ndcms-row ndcms-image-field' }, thumb, text, choose), value: () => text.value.trim() };
}

async function openSettings() {
  if (!state) return;
  const s = state;
  const fm = s.frontMatter;
  const str = (k: string) => (typeof fm[k] === 'string' || typeof fm[k] === 'number' ? String(fm[k]) : '');
  const list = (k: string) => (Array.isArray(fm[k]) ? (fm[k] as Json[]).map(String) : typeof fm[k] === 'string' ? [fm[k] as string] : []);

  const [tagTerms, categoryTerms, templates, authorList] = await Promise.all([
    api.taxonomy('tags').then((t) => t.terms.map((x) => x.name)).catch(() => [] as string[]),
    api.taxonomy('categories').then((t) => t.terms.map((x) => x.name)).catch(() => [] as string[]),
    pageTemplates(),
    api.blogAuthors().catch(() => [] as { id: string; name: string }[]),
  ]);
  const dateMap = typeof fm.date === 'object' && fm.date && !Array.isArray(fm.date) ? fm.date as Record<string, Json> : null;
  const isPost = /(^|\/)posts\//.test(s.page.path) || 'authors' in fm || 'categories' in fm || dateMap !== null;

  const title = input(str('title'));
  const navTitle = input(str('nav_title'), 'Empty: uses the title');
  const description = textarea(str('description'), 3, false);
  const tags = chipInput(list('tags'), tagTerms);
  const categories = chipInput(list('categories'), categoryTerms);
  const date = dateInput(dateMap ? String(dateMap.created ?? '') : str('date'));
  const updated = dateInput(dateMap ? String(dateMap.updated ?? '') : '');
  const authors = chipInput(list('authors'), authorList.map((a) => a.id));
  const slug = input(str('slug'), 'Empty: built from the title');
  const pageTitle = input(str('page_title'), 'Empty: uses the title');
  const rssTitle = input(str('rss_title'), 'Empty: uses the title');
  const rssDescription = textarea(str('rss_description'), 2, false);
  rssDescription.placeholder = 'Empty: uses the post\'s first paragraph';
  const tagsTitle = input(str('tags_title'), 'Empty: uses the title');
  const postImage = imageInput(str('image'), s.page.path);
  const redirectFrom = chipInput(list('redirect_from'), []);
  const draft = el('input', { type: 'checkbox' }) as HTMLInputElement;
  draft.checked = fm.draft === true;
  const icon = iconInput(str('icon'));
  const template = select(str('template'), ['', ...templates]);
  template.querySelector('option[value=""]')!.textContent = 'Default (main.html)';
  const hide = list('hide');
  const hideBoxes = ['toc', 'nav', 'path', 'footer', 'tags'].map((h) => {
    const c = el('input', { type: 'checkbox', value: h }) as HTMLInputElement;
    c.checked = hide.includes(h);
    return el('label', { class: 'ndcms-check' }, c, ' ' + h);
  });

  const customRows = el('div', { class: 'ndcms-custom' });
  const customs: { key: HTMLInputElement; value: HTMLTextAreaElement }[] = [];
  const addCustom = (k: string, v: Json | undefined) => {
    const row = { key: input(k, 'key'), value: textarea(v === undefined ? '' : typeof v === 'string' ? v : JSON.stringify(v), 1) };
    customs.push(row);
    const line = el('div', { class: 'ndcms-custom__row' }, row.key, row.value, button('✕', 'Remove', () => { customs.splice(customs.indexOf(row), 1); line.remove(); }, 'ndcms-btn--danger'));
    customRows.append(line);
  };
  for (const [k, v] of Object.entries(fm)) if (!knownKeys.includes(k)) addCustom(k, v);

  const section = (name: string, open: boolean, ...children: HTMLElement[]) => {
    const d = el('details', { class: 'ndcms-section' }, el('summary', {}, name), ...children);
    d.open = open;
    return d;
  };
  const authorHint = authorList.length
    ? 'Authors from .authors.yml: ' + authorList.map((a) => `${a.id} (${a.name})`).join(', ')
    : 'Author ids come from the blog\'s .authors.yml file; none was found.';

  openPanel('Page settings', el('div', {},
    section('Page', true,
      field('Title', title),
      field('Description', description, 'Used for search results and social cards'),
      field('Tags', tags.root),
      field('Image', postImage.root, 'Shown when the page is shared (og:image) and in the RSS feed. Empty: the social card, or for posts the first image.'),
    ),
    section('Blog post', isPost,
      field('Published', date.root, 'The post date shown on the page and used to sort the blog'),
      field('Updated', updated.root, 'Optional: when the post was last revised'),
      field('Authors', authors.root, authorHint),
      field('Categories', categories.root, 'The blog\'s category pages list the post under each one'),
      field('URL slug', slug, 'The last part of the post\'s address'),
      field('Title in the RSS feed', rssTitle),
      field('Summary in the RSS feed', rssDescription),
      el('label', { class: 'ndcms-check' }, draft, ' Draft (left out of the published blog)'),
    ),
    section('Navigation and layout', !isPost,
      field('Navigation title', navTitle, 'Shorter label for the navigation'),
      field('Browser tab title', pageTitle),
      field('Title on the tags page', tagsTitle, 'How this page is listed on the tag index'),
      field('Old addresses', redirectFrom.root, 'Paths that should forward here (redirect_from), such as old/page/. Needs the Redirects plugin.'),
      field('Icon', icon.root, 'Shown next to the page in the navigation'),
      field('Template', template, 'Page templates come from the theme and your overrides folder'),
      el('div', { class: 'ndcms-field' }, el('span', { class: 'ndcms-field__label' }, 'Hide parts of the page'), el('div', { class: 'ndcms-checks' }, ...hideBoxes)),
    ),
    section('Other attributes', customs.length > 0,
      el('div', { class: 'ndcms-field' }, el('small', { class: 'ndcms-muted' }, 'Any other front matter key. Values may be text or JSON (lists, numbers, true/false).'), customRows,
        button('+ Add attribute', 'Add a front matter key', () => addCustom('', ''))),
    ),
  ), [], () => {
      const next: FrontMatter = {};
      // Keep the file's key order: existing keys first, new keys after.
      const set = (k: string, v: Json | undefined) => {
        if (v === undefined || v === '' || (Array.isArray(v) && v.length === 0)) delete next[k];
        else next[k] = v;
      };
      for (const k of Object.keys(fm)) next[k] = fm[k];
      set('title', title.value.trim() || undefined);
      set('nav_title', navTitle.value.trim() || undefined);
      set('description', description.value.trim() || undefined);
      set('tags', tags.values());
      set('categories', categories.values());
      // A post with an update date uses the map form: date: { created, updated }.
      const created = date.value();
      const changed = updated.value();
      if (dateMap || changed) {
        const map: Record<string, Json> = { ...(dateMap ?? {}) };
        if (created) map.created = created; else delete map.created;
        if (changed) map.updated = changed; else delete map.updated;
        set('date', Object.keys(map).length === 0 ? undefined : Object.keys(map).length === 1 && map.created ? created : map);
      } else set('date', created || undefined);
      set('authors', authors.values());
      set('slug', slug.value.trim() || undefined);
      set('page_title', pageTitle.value.trim() || undefined);
      set('rss_title', rssTitle.value.trim() || undefined);
      set('rss_description', rssDescription.value.trim() || undefined);
      set('tags_title', tagsTitle.value.trim() || undefined);
      set('image', postImage.value() || undefined);
      set('redirect_from', redirectFrom.values());
      set('draft', draft.checked ? true : undefined);
      set('icon', icon.value() || undefined);
      set('template', template.value.trim() || undefined);
      const hidden = hideBoxes.map((l) => l.querySelector('input') as HTMLInputElement).filter((c) => c.checked).map((c) => c.value);
      set('hide', hidden.length ? hidden : undefined);
      for (const k of Object.keys(next)) if (!knownKeys.includes(k)) delete next[k];
      for (const row of customs) {
        const k = row.key.value.trim();
        if (!k) continue;
        const raw = row.value.value.trim();
        let v: Json = raw;
        if (/^[[{]|^(true|false|null|-?\d+(\.\d+)?)$/.test(raw)) { try { v = JSON.parse(raw); } catch { v = raw; } }
        set(k, v);
      }
      if (JSON.stringify(next) !== JSON.stringify(s.frontMatter)) {
        s.frontMatter = next;
        s.frontMatterChanged = JSON.stringify(next) !== JSON.stringify(s.page.frontMatter);
      }
      updateStatus();
  });
}

/** A date picker that keeps a time of day (2024-05-01 10:30) when the page has one. */
function dateInput(value: string) {
  const m = /^(\d{4}-\d{2}-\d{2})(?:[ T](\d{2}:\d{2})(?::\d{2})?)?$/.exec(value.trim());
  const root = el('div', { class: 'ndcms-row' });
  const day = el('input', { class: 'ndcms-input', type: 'date' }) as HTMLInputElement;
  const time = el('input', { class: 'ndcms-input', type: 'time', title: 'Time (optional)' }) as HTMLInputElement;
  const other = value.trim() !== '' && !m;
  if (m) { day.value = m[1]; time.value = m[2] ?? ''; }
  const text = input(other ? value : '', 'YYYY-MM-DD');
  const today = button('Today', 'Use today\'s date', () => {
    const d = new Date();
    day.value = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  });
  const clear = button('Clear', 'Remove the date', () => { day.value = ''; time.value = ''; text.value = ''; });
  if (other) root.append(text, clear);
  else root.append(day, time, today, clear);
  return {
    root,
    value: () => (other ? text.value.trim() : day.value ? (time.value ? `${day.value} ${time.value}` : day.value) : ''),
  };
}

/** An icon field: a preview, the name, and a button that opens the icon picker. */
function iconInput(value: string) {
  let current = value;
  const preview = el('span', { class: 'ndcms-icon-preview' });
  const name = el('span', { class: 'ndcms-icon-name' });
  const draw = async () => {
    name.textContent = current || 'No icon';
    preview.innerHTML = '';
    if (!current) return;
    const icons = await api.icons(current.split('/').pop() ?? current, 40).catch(() => []);
    preview.innerHTML = icons.find((i) => i.name === current)?.svg ?? '';
  };
  const pick = button('Choose…', 'Pick an icon', async () => {
    const chosen = await openIconPicker(current);
    if (chosen === null) return;
    current = chosen;
    void draw();
    root.dispatchEvent(new Event('change', { bubbles: true }));
  });
  void draw();
  const root = el('div', { class: 'ndcms-row ndcms-icon-field' }, preview, name, pick);
  return { root, value: () => current };
}

/** A tag-style input with suggestions from the site's existing terms. */
function chipInput(initial: string[], suggestions: string[]) {
  const values = [...initial];
  const root = el('div', { class: 'ndcms-chips' });
  const listId = 'ndcms-dl-' + Math.random().toString(36).slice(2);
  const entry = el('input', { class: 'ndcms-chips__input', list: listId, placeholder: 'Add…' }) as HTMLInputElement;
  const datalist = el('datalist', { id: listId }, ...suggestions.map((s) => el('option', { value: s })));
  const changed = () => root.dispatchEvent(new Event('change', { bubbles: true }));
  const draw = () => {
    root.querySelectorAll('.ndcms-chip').forEach((c) => c.remove());
    values.forEach((v, i) => root.insertBefore(el('span', { class: 'ndcms-chip' }, v, button('×', `Remove ${v}`, () => { values.splice(i, 1); draw(); changed(); })), entry));
  };
  // Reuse an existing term's exact spelling, so "docker" does not become a second tag.
  const typed = () => entry.value.split(',').map((x) => x.trim()).filter(Boolean)
    .map((part) => suggestions.find((s) => s.toLowerCase() === part.toLowerCase()) ?? part);
  const add = () => {
    const before = values.length;
    for (const t of typed()) if (!values.includes(t)) values.push(t);
    entry.value = '';
    draw();
    if (values.length !== before) changed();
  };
  entry.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); add(); }
    if (e.key === 'Backspace' && !entry.value && values.length) { values.pop(); draw(); changed(); }
  });
  entry.addEventListener('change', (e) => { e.stopPropagation(); add(); });
  root.append(entry, datalist);
  draw();
  // Text still being typed counts too, without turning it into a chip yet.
  return { root, values: () => [...new Set([...values, ...typed()])] };
}

// --- Saving -------------------------------------------------------------------------------------

async function save(overwrite = false) {
  if (!state) return;
  closeAllPanels();
  syncRichBlocks();
  if (!isDirty() && !overwrite) { updateStatus('Nothing to save'); return; }
  const s = state;
  // Overwriting sends every block's text, since the file's own blocks may have moved.
  const edits = s.blocks.flatMap((b) => (!overwrite && b.original !== null && !b.changed ? (b.span ?? [b.original]).map((keep): { keep?: number; source?: string } => ({ keep })) : [{ source: b.source }]));
  updateStatus('Saving…');
  try {
    const result = await api.saveBlocks(s.page.path, overwrite ? null : s.page.hash, edits, s.frontMatterChanged || overwrite ? s.frontMatter : null);
    if (state !== s) return;
    s.page = result.page;
    s.blocks = mergeWrappers(result.blocks).map(toEditorBlock);
    s.originalCount = result.blocks.length;
    s.frontMatter = structuredClone(result.page.frontMatter);
    s.frontMatterChanged = false;
    s.reloadPending = true;
    renderBlocks();
    updateStatus('Saved ✓', 'ok');
    notifyParent('saved');
  } catch (e) {
    if (e instanceof ApiError && e.status === 409) {
      updateStatus('Conflict', 'error');
      const choice = await conflictDialog();
      if (choice === 'reload') void discard();
      if (choice === 'overwrite') void save(true);
      return;
    }
    alertError(e);
  }
}

/** Asks what to do when the file changed on disk (another editor, git, a script) since it was opened. */
function conflictDialog(): Promise<'reload' | 'overwrite' | null> {
  return new Promise((resolve) => {
    const close = (v: 'reload' | 'overwrite' | null) => { back.remove(); resolve(v); };
    const back = el('div', { class: 'ndcms-modal' },
      el('div', { class: 'ndcms-modal__box', role: 'alertdialog' },
        el('h2', {}, 'This page changed on disk'),
        el('p', {}, 'The file was changed outside this editor (another editor, git, or a script) since it was loaded here.'),
        el('div', { class: 'ndcms-modal__actions' },
          button('Cancel', 'Keep editing without saving', () => close(null)),
          button('Reload the file', 'Load the version on disk. Your unsaved edits here are lost.', () => close('reload')),
          button('Save my version', 'Replace the file with what you see here', () => close('overwrite'), 'ndcms-btn--primary'),
        ),
      ),
    );
    document.body.append(back);
  });
}

function alertError(e: unknown) {
  const message = e instanceof Error ? e.message : String(e);
  updateStatus(message, 'error');
  console.error('[netdocs cms]', e);
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mountToolbar);
else mountToolbar();
