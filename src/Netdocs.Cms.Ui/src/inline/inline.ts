// In-page editor. `netdocs serve` adds this script to every page: it shows a small CMS toolbar
// and, in edit mode, turns the article into editable blocks. Plain text blocks are edited in place;
// admonitions, tabs, code, diagrams and anything else open a form or the block's markdown source.
// Saving sends only the changed blocks, so untouched markdown stays byte-for-byte the same.
import './inline.css';
import { api, cms, ApiError, type FrontMatter, type Json, type PageDocument, type RenderedBlock } from '../shared/api';
import {
  admonitionTypes, codeLanguages, formatAdmonition, formatFence, formatTabs, htmlToMarkdown, insertTemplates,
  needsSourceEditing, parseAdmonition, parseFence, parseTabs, richKinds,
} from '../shared/markdown';
import { imageMarkdown, openIconPicker, openImagePicker, openLinkPicker, pageTemplates, parseImageMarkdown } from '../shared/pickers';

interface EditorBlock {
  /** Index of the original block in the file, or null for a new block. */
  original: number | null;
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
function blockMenu(position: () => number, onPick: () => void): HTMLElement {
  const menu = el('div', { class: 'ndcms-insert__menu' });
  const filter = el('input', { type: 'search', placeholder: 'Find a block type…', class: 'ndcms-input' }) as HTMLInputElement;
  const list = el('div', { class: 'ndcms-insert__list' });
  const draw = () => {
    list.replaceChildren();
    const term = filter.value.toLowerCase();
    let group = '';
    for (const t of insertTemplates.filter((x) => !term || x.label.toLowerCase().includes(term) || x.group.toLowerCase().includes(term))) {
      if (t.group !== group) { group = t.group; list.append(el('div', { class: 'ndcms-insert__group' }, group)); }
      list.append(button(t.label, t.label, () => { onPick(); void insertTemplate(position(), t.id); }, 'ndcms-insert__item'));
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
  const originals = state.blocks.map((b) => b.original);
  return originals.some((o, i) => o !== i) || originals.length !== state.originalCount;
}

// --- Edit mode --------------------------------------------------------------------------------

async function enterEditMode() {
  const article = findArticle();
  if (!article || !cms.path) return;
  updateStatus('Loading…');
  try {
    const { page, blocks } = await api.blocks(cms.path);
    state = {
      page,
      blocks: blocks.map(toEditorBlock),
      frontMatter: structuredClone(page.frontMatter),
      frontMatterChanged: false,
      article,
      originalArticle: article.innerHTML,
      reloadPending: false,
      originalCount: blocks.length,
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

function toEditorBlock(b: RenderedBlock): EditorBlock {
  const rich = isRich(b.kind, b.source, b.html);
  return { original: b.index, kind: b.kind, source: b.source, html: b.html, changed: false, rich };
}

async function exitEditMode(force = false) {
  if (!state || embedded) return;
  if (!force && isDirty() && !confirm('Leave edit mode and lose your unsaved changes?')) return;
  const reload = state.reloadPending || state.blocks.some((b) => b.changed);
  state.article.innerHTML = state.originalArticle;
  state = null;
  document.documentElement.classList.remove('ndcms-editing');
  closePanel();
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
  if (s.blocks.length === 0) {
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
  markActive();
  refreshThemeWidgets(s.article);
  updateStatus();
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
    content.addEventListener('click', (e) => {
      // Let tabs, collapsibles and copy buttons work; anything else opens the editor.
      const t = e.target as HTMLElement;
      if (t.closest('label, summary, button, input, .tabbed-labels')) return;
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
  abbreviations: 'Abbreviations', math: 'Math',
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
  // Select the placeholder text so typing replaces it.
  const content = block.content;
  if (!content) return;
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
  const { blocks } = await api.renderBlocks(state.page.path, block.source, state.frontMatter);
  block.html = blocks.map((b) => b.html).join('\n');
  const kinds = blocks.map((b) => b.kind);
  block.kind = kinds.length === 1 ? kinds[0] : kinds.length === 0 ? 'empty' : 'mixed';
  block.rich = kinds.length === 1 && isRich(block.kind, block.source, block.html);
}

/** Text-like blocks are edited in place; tables, images and the rest use a form. */
function isRich(kind: string, source: string, html: string): boolean {
  if (kind === 'table') return false;
  if (parseImageMarkdown(source, state?.page.path ?? cms.path ?? '')) return false;
  return richKinds.has(kind) && !needsSourceEditing(kind, source, html);
}

/** Lets Material's own scripts (tabs, code copy, tooltips, diagrams) pick up re-rendered content. */
function refreshThemeWidgets(root: HTMLElement) {
  const w = window as unknown as { document$?: { next: (d: Document) => void }; mermaid?: { run?: (o: unknown) => Promise<void> }; hljs?: { highlightElement: (e: Element) => void } };
  try { w.document$?.next(document); } catch { /* theme scripts are optional */ }
  try { w.mermaid?.run?.({ nodes: root.querySelectorAll('.mermaid:not([data-processed])') }); } catch { /* optional */ }
  try { root.querySelectorAll('pre code').forEach((c) => w.hljs?.highlightElement(c)); } catch { /* optional */ }
}

// --- Rich text formatting bar ------------------------------------------------------------------

let formatBar: HTMLElement | null = null;

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
    button('H2', 'Heading 2', () => exec('formatBlock', 'h2')),
    button('H3', 'Heading 3', () => exec('formatBlock', 'h3')),
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

function wrapSelection(tag: string, target: HTMLElement) {
  const sel = window.getSelection();
  if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return;
  const range = sel.getRangeAt(0);
  if (!target.contains(range.commonAncestorContainer)) return;
  const node = document.createElement(tag);
  node.appendChild(range.extractContents());
  range.insertNode(node);
  target.dispatchEvent(new Event('input'));
}

// --- Side panel: block editors and page settings -------------------------------------------------

let panel: HTMLElement | null = null;

function closePanel() {
  panel?.remove();
  panel = null;
}

function openPanel(title: string, body: HTMLElement, actions: HTMLElement[]) {
  closePanel();
  panel = el('aside', { class: 'ndcms-panel', role: 'dialog', 'aria-label': title },
    el('header', { class: 'ndcms-panel__head' }, el('h2', {}, title), button('✕', 'Close (Esc)', closePanel)),
    el('div', { class: 'ndcms-panel__body' }, body),
    el('footer', { class: 'ndcms-panel__foot' }, ...actions),
  );
  panel.addEventListener('keydown', (e) => { if (e.key === 'Escape') closePanel(); });
  document.body.append(panel);
  panel.querySelector<HTMLElement>('input, textarea, select')?.focus();
}

const field = (labelText: string, control: HTMLElement, hint?: string) =>
  el('label', { class: 'ndcms-field' }, el('span', { class: 'ndcms-field__label' }, labelText), control, ...(hint ? [el('small', { class: 'ndcms-muted' }, hint)] : []));

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
  const apply = async (source: string) => {
    if (source.trim() === block.source.trim()) { closePanel(); return; }
    block.source = source;
    block.changed = true;
    try {
      await renderSource(block);
    } catch (e) {
      alertError(e);
      return;
    }
    closePanel();
    if (s === state) renderBlocks();
  };

  const image = !forceSource ? parseImageMarkdown(block.source, s.page.path) : null;
  if (image) {
    void openImagePicker(s.page.path, image).then((picked) => { if (picked) void apply(imageMarkdown(picked)); });
    return;
  }
  const table = !forceSource && block.kind === 'table' ? parseTable(block.source) : null;
  if (table) {
    const editor = tableEditor(table);
    openPanel('Table', editor.root, [
      button('Edit source', 'Edit the raw markdown', () => openBlockEditor(index, true)),
      button('Apply', 'Apply to the page', () => void apply(formatTable(editor.value())), 'ndcms-btn--primary'),
    ]);
    return;
  }

  const admonition = !forceSource ? parseAdmonition(block.source) : null;
  const fence = !forceSource && !admonition ? parseFence(block.source) : null;
  const tabs = !forceSource && !admonition && !fence ? parseTabs(block.source) : null;

  if (admonition) {
    const type = select(admonition.type, admonitionTypes);
    const style = select(admonition.marker, ['!!!', '???', '???+']);
    const title = input(admonition.title ?? '', 'Leave empty for the default title');
    const noTitle = el('input', { type: 'checkbox' }) as HTMLInputElement;
    noTitle.checked = admonition.title === '';
    const body = richArea(admonition.body, s.page.path);
    openPanel('Callout box', el('div', {},
      field('Type', type),
      field('Style', style, '!!! always open · ??? collapsed · ???+ collapsible, open'),
      field('Title', title),
      el('label', { class: 'ndcms-check' }, noTitle, ' Hide the title bar'),
      field('Content', body.root),
    ), [
      button('Edit source', 'Edit the raw markdown', () => openBlockEditor(index, true)),
      button('Apply', 'Apply to the page', () => void apply(formatAdmonition({
        marker: style.value as '!!!', type: type.value, extra: admonition.extra,
        title: noTitle.checked ? '' : title.value.trim() ? title.value : null, body: body.value(),
      })), 'ndcms-btn--primary'),
    ]);
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
      button('Edit source', 'Edit the raw markdown', () => openBlockEditor(index, true)),
      button('Apply', 'Apply to the page', () => void apply(formatFence({ fence: fence.fence, lang: lang.value.trim(), options: options.value.trim(), code: code.value })), 'ndcms-btn--primary'),
    ]);
    return;
  }

  if (tabs) {
    const list = el('div', { class: 'ndcms-tabs-editor' });
    const rows: { title: HTMLInputElement; body: RichArea; selected: boolean }[] = [];
    const addRow = (t: { title: string; body: string; selected: boolean }) => {
      const row = { title: input(t.title, 'Tab title'), body: richArea(t.body, s.page.path), selected: t.selected };
      rows.push(row);
      const box = el('fieldset', { class: 'ndcms-tab' }, el('legend', {}, 'Tab'), field('Title', row.title), field('Content', row.body.root),
        button('Remove tab', 'Remove this tab', () => { rows.splice(rows.indexOf(row), 1); box.remove(); }, 'ndcms-btn--danger'));
      list.append(box);
    };
    tabs.forEach(addRow);
    openPanel('Content tabs', el('div', {}, list, button('+ Add tab', 'Add another tab', () => addRow({ title: 'New tab', body: 'Content.', selected: false }))), [
      button('Edit source', 'Edit the raw markdown', () => openBlockEditor(index, true)),
      button('Apply', 'Apply to the page', () => void apply(formatTabs(rows.map((r) => ({ title: r.title.value, body: r.body.value(), selected: r.selected })))), 'ndcms-btn--primary'),
    ]);
    return;
  }

  const source = textarea(block.source, 18);
  const preview = el('div', { class: 'ndcms-preview md-typeset' });
  let timer = 0;
  source.addEventListener('input', () => {
    clearTimeout(timer);
    timer = window.setTimeout(async () => {
      try {
        const { blocks } = await api.renderBlocks(s.page.path, source.value, s.frontMatter);
        preview.innerHTML = blocks.map((b) => b.html).join('\n');
        refreshThemeWidgets(preview);
      } catch { /* keep the last preview */ }
    }, 350);
  });
  preview.innerHTML = block.html;
  openPanel(`${label(block.kind)} · markdown`, el('div', {}, field('Markdown', source, 'Any Netdocs markdown works here: admonitions, tabs, attr lists, icons, macros…'), el('div', { class: 'ndcms-field__label' }, 'Preview'), preview), [
    button('Apply', 'Apply to the page', () => void apply(source.value), 'ndcms-btn--primary'),
  ]);
}

// --- Rich text areas inside forms ----------------------------------------------------------------

interface RichArea { root: HTMLElement; value: () => string }

/**
 * Edits a piece of markdown (an admonition or tab body) visually when it only holds text-like
 * blocks, and as markdown otherwise. "Edit as markdown" switches over at any time.
 */
function richArea(markdown: string, pagePath: string): RichArea {
  const root = el('div', { class: 'ndcms-rich' });
  const source = textarea(markdown, 8);
  const editable = el('div', { class: 'ndcms-rich__text md-typeset', contenteditable: 'true', spellcheck: 'true' });
  let mode: 'rich' | 'source' | 'loading' = 'loading';
  let renderedHtml = '';
  let original = markdown;
  const toggle = button('Edit as markdown', 'Switch between visual and markdown editing', () => {
    if (mode === 'rich') { source.value = current(); showSource(); }
    else if (mode === 'source') void tryRich(source.value);
  }, 'ndcms-btn--link');
  const current = () => {
    if (mode !== 'rich') return source.value;
    if (editable.innerHTML === renderedHtml) return original;
    return htmlToMarkdown(editable.innerHTML);
  };
  const showSource = () => {
    mode = 'source';
    toggle.textContent = 'Edit visually';
    root.replaceChildren(source, toggle);
  };
  const tryRich = async (md: string) => {
    try {
      const { blocks } = await api.renderBlocks(pagePath, md, state?.frontMatter ?? {});
      const safe = blocks.every((b) => isRich(b.kind, b.source, b.html));
      if (!safe) {
        showSource();
        root.append(el('small', { class: 'ndcms-muted' }, ' This content has blocks that are edited as markdown.'));
        return;
      }
      editable.innerHTML = blocks.length ? blocks.map((b) => b.html).join('\n') : '<p><br></p>';
      renderedHtml = editable.innerHTML;
      original = md;
      mode = 'rich';
      toggle.textContent = 'Edit as markdown';
      root.replaceChildren(editable, toggle);
    } catch {
      showSource();
    }
  };
  editable.addEventListener('focus', () => showFormatBar(editable, root));
  editable.addEventListener('click', (e) => { if ((e.target as HTMLElement).closest('a')) e.preventDefault(); });
  root.append(el('span', { class: 'ndcms-muted' }, 'Loading…'));
  void tryRich(markdown);
  return { root, value: current };
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
const knownKeys = ['title', 'description', 'tags', 'categories', 'date', 'draft', 'icon', 'template', 'hide', 'nav_title', 'page_title', 'authors', 'slug'];

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
  const navTitle = input(str('nav_title'), 'Shorter label for the navigation');
  const description = textarea(str('description'), 3, false);
  const tags = chipInput(list('tags'), tagTerms);
  const categories = chipInput(list('categories'), categoryTerms);
  const date = dateInput(dateMap ? String(dateMap.created ?? '') : str('date'));
  const updated = dateInput(dateMap ? String(dateMap.updated ?? '') : '');
  const authors = chipInput(list('authors'), authorList.map((a) => a.id));
  const slug = input(str('slug'), 'Leave empty to build it from the title');
  const pageTitle = input(str('page_title'), 'Leave empty to use the title');
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
    ),
    section('Blog post', isPost,
      field('Published', date.root, 'The post date shown on the page and used to sort the blog'),
      field('Updated', updated.root, 'Optional: when the post was last revised'),
      field('Authors', authors.root, authorHint),
      field('Categories', categories.root, 'The blog\'s category pages list the post under each one'),
      field('URL slug', slug, 'The last part of the post\'s address'),
      el('label', { class: 'ndcms-check' }, draft, ' Draft (left out of the published blog)'),
    ),
    section('Navigation and layout', !isPost,
      field('Navigation title', navTitle, 'Shorter label for the navigation'),
      field('Browser tab title', pageTitle),
      field('Icon', icon.root, 'Shown next to the page in the navigation'),
      field('Template', template, 'Page templates come from the theme and your overrides folder'),
      el('div', { class: 'ndcms-field' }, el('span', { class: 'ndcms-field__label' }, 'Hide parts of the page'), el('div', { class: 'ndcms-checks' }, ...hideBoxes)),
    ),
    section('Other attributes', customs.length > 0,
      el('div', { class: 'ndcms-field' }, el('small', { class: 'ndcms-muted' }, 'Any other front matter key. Values may be text or JSON (lists, numbers, true/false).'), customRows,
        button('+ Add attribute', 'Add a front matter key', () => addCustom('', ''))),
    ),
  ), [
    button('Apply', 'Apply to the page (then Save)', () => {
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
      if (JSON.stringify(next) !== JSON.stringify(fm)) {
        s.frontMatter = next;
        s.frontMatterChanged = true;
      }
      closePanel();
      updateStatus();
    }, 'ndcms-btn--primary'),
  ]);
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
  });
  void draw();
  return { root: el('div', { class: 'ndcms-row ndcms-icon-field' }, preview, name, pick), value: () => current };
}

/** A tag-style input with suggestions from the site's existing terms. */
function chipInput(initial: string[], suggestions: string[]) {
  const values = [...initial];
  const root = el('div', { class: 'ndcms-chips' });
  const listId = 'ndcms-dl-' + Math.random().toString(36).slice(2);
  const entry = el('input', { class: 'ndcms-chips__input', list: listId, placeholder: 'Add…' }) as HTMLInputElement;
  const datalist = el('datalist', { id: listId }, ...suggestions.map((s) => el('option', { value: s })));
  const draw = () => {
    root.querySelectorAll('.ndcms-chip').forEach((c) => c.remove());
    values.forEach((v, i) => root.insertBefore(el('span', { class: 'ndcms-chip' }, v, button('×', `Remove ${v}`, () => { values.splice(i, 1); draw(); })), entry));
  };
  const add = () => {
    for (const part of entry.value.split(',').map((x) => x.trim()).filter(Boolean)) {
      // Reuse an existing term's exact spelling, so "docker" does not become a second tag.
      const match = suggestions.find((s) => s.toLowerCase() === part.toLowerCase()) ?? part;
      if (!values.includes(match)) values.push(match);
    }
    entry.value = '';
    draw();
  };
  entry.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); add(); }
    if (e.key === 'Backspace' && !entry.value && values.length) { values.pop(); draw(); }
  });
  entry.addEventListener('change', add);
  root.append(entry, datalist);
  draw();
  return { root, values: () => { add(); return [...values]; } };
}

// --- Saving -------------------------------------------------------------------------------------

async function save() {
  if (!state) return;
  syncRichBlocks();
  if (!isDirty()) { updateStatus('Nothing to save'); return; }
  const s = state;
  const edits = s.blocks.map((b) => (b.original !== null && !b.changed ? { keep: b.original } : { source: b.source }));
  updateStatus('Saving…');
  try {
    const result = await api.saveBlocks(s.page.path, s.page.hash, edits, s.frontMatterChanged ? s.frontMatter : null);
    if (state !== s) return;
    s.page = result.page;
    s.blocks = result.blocks.map(toEditorBlock);
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
      if (confirm(`${e.message}\n\nReload the page now? Your unsaved edits will be lost.`)) void discard();
      return;
    }
    alertError(e);
  }
}

function alertError(e: unknown) {
  const message = e instanceof Error ? e.message : String(e);
  updateStatus(message, 'error');
  console.error('[netdocs cms]', e);
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mountToolbar);
else mountToolbar();
