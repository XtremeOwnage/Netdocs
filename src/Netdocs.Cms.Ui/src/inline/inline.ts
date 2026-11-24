// In-page editor. `netdocs serve` adds this script to every page: it shows a small CMS toolbar
// and, in edit mode, turns the article into editable blocks. Plain text blocks are edited in place;
// admonitions, tabs, code, diagrams and anything else open a form or the block's markdown source.
// Saving sends only the changed blocks, so untouched markdown stays byte-for-byte the same.
import './inline.css';
import { api, cms, ApiError, type FrontMatter, type Json, type PageDocument, type RenderedBlock } from '../shared/api';
import {
  admonitionTypes, formatAdmonition, formatFence, formatTabs, htmlToMarkdown, insertTemplates,
  needsSourceEditing, parseAdmonition, parseFence, parseTabs, richKinds,
} from '../shared/markdown';

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
  bar = el('div', { class: 'ndcms-bar', role: 'toolbar', 'aria-label': 'Netdocs CMS' });
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
    if (isDirty()) { e.preventDefault(); e.returnValue = ''; }
  });

  // While editing, the dev server's live reload waits until the editor is closed.
  (window as unknown as { __netdocsCmsHoldReload: () => boolean }).__netdocsCmsHoldReload = () => {
    if (!state) return false;
    state.reloadPending = true;
    return true;
  };

  const params = new URLSearchParams(location.search);
  if (cms.path && (sessionStorage.getItem('ndcms-edit') === cms.path || params.has('cms-edit'))) {
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
  bar.append(
    el('span', { class: 'ndcms-path', title: state.page.path }, state.page.path),
    status,
    button('⚙ Page settings', 'Title, tags, metadata', () => openSettings()),
    button('↶ Discard', 'Throw away unsaved changes', () => void discard()),
    button('💾 Save', 'Save changes (Ctrl+S)', () => void save(), 'ndcms-btn--primary'),
    button('✕', 'Leave edit mode', () => void exitEditMode()),
  );
  updateStatus();
}

function updateStatus(message?: string, kind: 'info' | 'error' | 'ok' = 'info') {
  if (!status) return;
  status.className = `ndcms-status ndcms-status--${kind}`;
  status.textContent = message ?? (isDirty() ? 'Unsaved changes' : 'No changes');
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
  } catch (e) {
    alertError(e);
  }
}

function toEditorBlock(b: RenderedBlock): EditorBlock {
  const rich = richKinds.has(b.kind) && !needsSourceEditing(b.kind, b.source, b.html);
  return { original: b.index, kind: b.kind, source: b.source, html: b.html, changed: false, rich };
}

async function exitEditMode(force = false) {
  if (!state) return;
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
  if (s.blocks.length === 0) s.article.append(el('p', { class: 'ndcms-muted' }, 'This page is empty. Use + to add a block.'));
  refreshThemeWidgets(s.article);
  updateStatus();
}

function renderBlock(block: EditorBlock, index: number): HTMLElement {
  const wrapper = el('div', { class: `ndcms-block ndcms-block--${block.kind}${block.changed || block.original === null ? ' ndcms-block--changed' : ''}`, 'data-index': String(index) });
  const content = el('div', { class: 'ndcms-block__content' });
  content.innerHTML = block.html || `<p class="ndcms-muted">(${block.kind} block with no visible output)</p>`;
  block.el = wrapper;
  block.content = content;
  block.renderedHtml = content.innerHTML;

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
      if ((e.target as HTMLElement).closest('a')) e.preventDefault();
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
  const toggle = button('+', 'Add a block here', () => {
    const open = wrap.classList.toggle('ndcms-insert--open');
    if (open) menu.querySelector<HTMLInputElement>('input')?.focus();
  }, 'ndcms-insert__toggle');
  const menu = el('div', { class: 'ndcms-insert__menu' });
  const filter = el('input', { type: 'search', placeholder: 'Find a block type…', class: 'ndcms-input' }) as HTMLInputElement;
  const list = el('div', { class: 'ndcms-insert__list' });
  const draw = () => {
    list.replaceChildren();
    const term = filter.value.toLowerCase();
    let group = '';
    for (const t of insertTemplates.filter((x) => !term || x.label.toLowerCase().includes(term) || x.group.toLowerCase().includes(term))) {
      if (t.group !== group) { group = t.group; list.append(el('div', { class: 'ndcms-insert__group' }, group)); }
      list.append(button(t.label, t.label, () => void insert(position, t.markdown), 'ndcms-insert__item'));
    }
  };
  filter.addEventListener('input', draw);
  draw();
  menu.append(filter, list);
  wrap.append(toggle, menu);
  return wrap;
}

async function insert(position: number, markdown: string) {
  if (!state) return;
  syncRichBlocks();
  const block: EditorBlock = { original: null, kind: 'new', source: markdown, html: '', changed: true, rich: false };
  await renderSource(block);
  state.blocks.splice(position, 0, block);
  renderBlocks();
  if (!block.rich) openBlockEditor(position);
  else block.content?.focus();
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
  block.rich = kinds.length === 1 && richKinds.has(block.kind) && !needsSourceEditing(block.kind, block.source, block.html);
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

function showFormatBar(target: HTMLElement) {
  formatBar?.remove();
  const exec = (cmd: string, value?: string) => { document.execCommand(cmd, false, value); target.dispatchEvent(new Event('input')); };
  formatBar = el('div', { class: 'ndcms-format' },
    button('<b>B</b>', 'Bold (Ctrl+B)', () => exec('bold')),
    button('<i>I</i>', 'Italic (Ctrl+I)', () => exec('italic')),
    button('<s>S</s>', 'Strikethrough', () => exec('strikeThrough')),
    button('<code>{}</code>', 'Inline code', () => wrapSelection('code', target)),
    button('<mark>H</mark>', 'Highlight', () => wrapSelection('mark', target)),
    button('🔗', 'Link', () => {
      const url = prompt('Link to (a page like other.md, or a URL):');
      if (url) exec('createLink', url);
    }),
    button('•', 'Bulleted list', () => exec('insertUnorderedList')),
    button('1.', 'Numbered list', () => exec('insertOrderedList')),
    button('H2', 'Heading 2', () => exec('formatBlock', 'h2')),
    button('H3', 'Heading 3', () => exec('formatBlock', 'h3')),
    button('¶', 'Paragraph', () => exec('formatBlock', 'p')),
    button('⌫', 'Clear formatting', () => exec('removeFormat')),
  );
  target.closest('.ndcms-block')?.prepend(formatBar);
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

  const admonition = !forceSource ? parseAdmonition(block.source) : null;
  const fence = !forceSource && !admonition ? parseFence(block.source) : null;
  const tabs = !forceSource && !admonition && !fence ? parseTabs(block.source) : null;

  if (admonition) {
    const type = select(admonition.type, admonitionTypes);
    const style = select(admonition.marker, ['!!!', '???', '???+']);
    const title = input(admonition.title ?? '', 'Leave empty for the default title');
    const noTitle = el('input', { type: 'checkbox' }) as HTMLInputElement;
    noTitle.checked = admonition.title === '';
    const body = textarea(admonition.body, 12);
    openPanel('Admonition', el('div', {},
      field('Type', type),
      field('Style', style, '!!! always open · ??? collapsed · ???+ collapsible, open'),
      field('Title', title),
      el('label', { class: 'ndcms-check' }, noTitle, ' Hide the title bar'),
      field('Content (markdown)', body),
    ), [
      button('Edit source', 'Edit the raw markdown', () => openBlockEditor(index, true)),
      button('Apply', 'Apply to the page', () => void apply(formatAdmonition({
        marker: style.value as '!!!', type: type.value, extra: admonition.extra,
        title: noTitle.checked ? '' : title.value.trim() ? title.value : null, body: body.value,
      })), 'ndcms-btn--primary'),
    ]);
    return;
  }

  if (fence) {
    const lang = input(fence.lang, 'python, yaml, mermaid…');
    const options = input(fence.options, 'title="file.py" linenums="1" hl_lines="2 3"');
    const code = textarea(fence.code, 16);
    openPanel(fence.lang === 'mermaid' ? 'Mermaid diagram' : 'Code block', el('div', {},
      field('Language', lang),
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
    const rows: { title: HTMLInputElement; body: HTMLTextAreaElement; selected: boolean }[] = [];
    const addRow = (t: { title: string; body: string; selected: boolean }) => {
      const row = { title: input(t.title, 'Tab title'), body: textarea(t.body, 6), selected: t.selected };
      rows.push(row);
      const box = el('fieldset', { class: 'ndcms-tab' }, el('legend', {}, 'Tab'), field('Title', row.title), field('Content (markdown)', row.body),
        button('Remove tab', 'Remove this tab', () => { rows.splice(rows.indexOf(row), 1); box.remove(); }, 'ndcms-btn--danger'));
      list.append(box);
    };
    tabs.forEach(addRow);
    openPanel('Content tabs', el('div', {}, list, button('+ Add tab', 'Add another tab', () => addRow({ title: 'New tab', body: 'Content.', selected: false }))), [
      button('Edit source', 'Edit the raw markdown', () => openBlockEditor(index, true)),
      button('Apply', 'Apply to the page', () => void apply(formatTabs(rows.map((r) => ({ title: r.title.value, body: r.body.value, selected: r.selected })))), 'ndcms-btn--primary'),
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

// Front matter keys with their own controls; anything else is shown as a custom attribute.
const knownKeys = ['title', 'description', 'tags', 'categories', 'date', 'draft', 'icon', 'template', 'hide', 'nav_title', 'page_title', 'authors', 'status'];

async function openSettings() {
  if (!state) return;
  const s = state;
  const fm = s.frontMatter;
  const str = (k: string) => (typeof fm[k] === 'string' || typeof fm[k] === 'number' ? String(fm[k]) : '');
  const list = (k: string) => (Array.isArray(fm[k]) ? (fm[k] as Json[]).map(String) : typeof fm[k] === 'string' ? [fm[k] as string] : []);

  const [tagTerms, categoryTerms] = await Promise.all([
    api.taxonomy('tags').then((t) => t.terms.map((x) => x.name)).catch(() => [] as string[]),
    api.taxonomy('categories').then((t) => t.terms.map((x) => x.name)).catch(() => [] as string[]),
  ]);

  const title = input(str('title'));
  const navTitle = input(str('nav_title'), 'Shorter label for the navigation');
  const description = textarea(str('description'), 3, false);
  const tags = chipInput(list('tags'), tagTerms);
  const categories = chipInput(list('categories'), categoryTerms);
  const dateValue = typeof fm.date === 'object' && fm.date && !Array.isArray(fm.date) ? String((fm.date as Record<string, Json>).created ?? '') : str('date');
  const date = input(dateValue, 'YYYY-MM-DD');
  const draft = el('input', { type: 'checkbox' }) as HTMLInputElement;
  draft.checked = fm.draft === true;
  const icon = input(str('icon'), 'material/file-document');
  const template = input(str('template'), 'main.html');
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

  openPanel('Page settings', el('div', {},
    field('Title', title),
    field('Navigation title', navTitle),
    field('Description', description, 'Used for search results and social cards'),
    field('Tags', tags.root),
    field('Categories', categories.root, 'Blog post categories'),
    field('Date', date),
    el('label', { class: 'ndcms-check' }, draft, ' Draft (hidden from the published blog)'),
    field('Icon', icon),
    field('Template', template),
    el('div', { class: 'ndcms-field' }, el('span', { class: 'ndcms-field__label' }, 'Hide'), el('div', { class: 'ndcms-checks' }, ...hideBoxes)),
    el('div', { class: 'ndcms-field' }, el('span', { class: 'ndcms-field__label' }, 'Custom attributes'), el('small', { class: 'ndcms-muted' }, 'Values may be text or JSON (lists, numbers, true/false).'), customRows,
      button('+ Add attribute', 'Add a front matter key', () => addCustom('', ''))),
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
      if (typeof fm.date === 'object' && fm.date && !Array.isArray(fm.date)) set('date', { ...(fm.date as Record<string, Json>), created: date.value.trim() });
      else set('date', date.value.trim() || undefined);
      set('draft', draft.checked ? true : undefined);
      set('icon', icon.value.trim() || undefined);
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
