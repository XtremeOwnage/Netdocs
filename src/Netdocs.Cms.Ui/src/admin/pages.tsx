// Pages list, page creation/move/delete, bulk tagging, and the full-page source editor.
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { api, type FrontMatter, type Json, type PageDocument, type PageSummary } from '../shared/api';
import { ChipInput, CodeEditor, Field, Loading, Modal, confirmAction, openInPageEditor, toast, toastError, useAction, useLoad } from './ui';
import { navigate } from './router';

export function PagesScreen() {
  const { data, error, reload } = useLoad(() => api.pages());
  const [filter, setFilter] = useState('');
  const [tag, setTag] = useState('');
  const [onlyUnlisted, setOnlyUnlisted] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [dialog, setDialog] = useState<null | { kind: 'new' } | { kind: 'move'; page: PageSummary } | { kind: 'tags' }>(null);
  const { run } = useAction();

  const allTags = useMemo(() => [...new Set((data ?? []).flatMap((p) => p.tags))].sort((a, b) => a.localeCompare(b)), [data]);
  const rows = useMemo(() => (data ?? []).filter((p) => {
    const term = filter.toLowerCase();
    return (!term || p.path.toLowerCase().includes(term) || p.title.toLowerCase().includes(term))
      && (!tag || p.tags.includes(tag))
      && (!onlyUnlisted || !p.inNav);
  }), [data, filter, tag, onlyUnlisted]);

  if (!data) return <Loading error={error} />;

  const toggle = (path: string) => {
    const next = new Set(selected);
    if (next.has(path)) next.delete(path); else next.add(path);
    setSelected(next);
  };

  const remove = async (p: PageSummary) => {
    const refs = await api.references(p.path).catch(() => [] as string[]);
    const warning = refs.length ? `\n\n${refs.length} page(s) link to it: ${refs.slice(0, 5).join(', ')}` : '';
    if (!confirmAction(`Delete ${p.path}? It is also removed from the navigation.${warning}`)) return;
    await run(() => api.deletePage(p.path), `Deleted ${p.path}`);
    reload();
  };

  return (
    <section>
      <header class="screen-head">
        <h1>Pages <span class="muted">({data.length})</span></h1>
        <button class="btn btn--primary" onClick={() => setDialog({ kind: 'new' })}>+ New page</button>
      </header>
      <div class="toolbar">
        <input class="input" type="search" placeholder="Filter by title or path…" value={filter} onInput={(e) => setFilter((e.target as HTMLInputElement).value)} />
        <select class="input input--auto" value={tag} onChange={(e) => setTag((e.target as HTMLSelectElement).value)}>
          <option value="">All tags</option>
          {allTags.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
        <label class="check"><input type="checkbox" checked={onlyUnlisted} onChange={() => setOnlyUnlisted(!onlyUnlisted)} /> Not in navigation</label>
        {selected.size > 0 && <button class="btn" onClick={() => setDialog({ kind: 'tags' })}>Tag {selected.size} selected…</button>}
      </div>
      <table class="table">
        <thead><tr><th><input type="checkbox" aria-label="Select all" checked={rows.length > 0 && rows.every((r) => selected.has(r.path))} onChange={(e) => setSelected((e.target as HTMLInputElement).checked ? new Set(rows.map((r) => r.path)) : new Set())} /></th><th>Title</th><th>Path</th><th>Tags</th><th>Updated</th><th /></tr></thead>
        <tbody>
          {rows.map((p) => (
            <tr key={p.path}>
              <td><input type="checkbox" checked={selected.has(p.path)} onChange={() => toggle(p.path)} aria-label={`Select ${p.path}`} /></td>
              <td>
                <a href={`#/pages/edit?path=${encodeURIComponent(p.path)}`}>{p.title}</a>
                {p.draft && <span class="badge badge--warn">draft</span>}
                {!p.inNav && <span class="badge" title="Not listed in the navigation">unlisted</span>}
              </td>
              <td class="mono">{p.path}</td>
              <td>{p.tags.map((t) => <span class="tag" key={t}>{t}</span>)}</td>
              <td class="muted nowrap">{new Date(p.modified).toLocaleString()}</td>
              <td class="actions">
                <button class="btn btn--small" disabled={p.url === null} title="Edit visually on the page" onClick={() => openInPageEditor(p.path, p.url)}>✎ Visual</button>
                <a class="btn btn--small" href={`#/pages/edit?path=${encodeURIComponent(p.path)}`}>Source</a>
                {p.url !== null && <a class="btn btn--small" href={'/' + p.url} target="_blank" rel="noopener">View</a>}
                <button class="btn btn--small" onClick={() => setDialog({ kind: 'move', page: p })}>Rename</button>
                <button class="btn btn--small btn--danger" onClick={() => void remove(p)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length === 0 && <p class="muted">No pages match.</p>}
      {dialog?.kind === 'new' && <NewPageDialog onClose={() => setDialog(null)} onCreated={(doc) => { setDialog(null); navigate(`/pages/edit?path=${encodeURIComponent(doc.path)}`); }} />}
      {dialog?.kind === 'move' && <MovePageDialog page={dialog.page} onClose={() => setDialog(null)} onMoved={() => { setDialog(null); reload(); }} />}
      {dialog?.kind === 'tags' && <BulkTagDialog pages={[...selected]} suggestions={allTags} onClose={() => setDialog(null)} onDone={() => { setDialog(null); setSelected(new Set()); reload(); }} />}
    </section>
  );
}

function NewPageDialog(props: { onClose: () => void; onCreated: (doc: PageDocument) => void }) {
  const [title, setTitle] = useState('');
  const [path, setPath] = useState('');
  const [touched, setTouched] = useState(false);
  const [addToNav, setAddToNav] = useState(true);
  const { busy, run } = useAction();
  const slug = (t: string) => t.toLowerCase().normalize('NFKD').replace(/[^\w\s-]/g, '').trim().replace(/[\s_]+/g, '-');
  return (
    <Modal title="New page" onClose={props.onClose} actions={<>
      <button class="btn" onClick={props.onClose}>Cancel</button>
      <button class="btn btn--primary" disabled={busy || !path} onClick={async () => {
        const doc = await run(() => api.createPage(path, title, addToNav), 'Page created');
        if (doc) props.onCreated(doc);
      }}>Create</button>
    </>}>
      <Field label="Title"><input class="input" value={title} autoFocus onInput={(e) => { const t = (e.target as HTMLInputElement).value; setTitle(t); if (!touched) setPath(slug(t) ? slug(t) + '.md' : ''); }} /></Field>
      <Field label="File" hint="Inside the docs folder, e.g. guides/setup.md or blog/posts/my-post.md"><input class="input mono" value={path} onInput={(e) => { setTouched(true); setPath((e.target as HTMLInputElement).value); }} /></Field>
      <label class="check"><input type="checkbox" checked={addToNav} onChange={() => setAddToNav(!addToNav)} /> Add to the end of the navigation</label>
    </Modal>
  );
}

function MovePageDialog(props: { page: PageSummary; onClose: () => void; onMoved: () => void }) {
  const [to, setTo] = useState(props.page.path);
  const { busy, run } = useAction();
  return (
    <Modal title={`Rename or move ${props.page.path}`} onClose={props.onClose} actions={<>
      <button class="btn" onClick={props.onClose}>Cancel</button>
      <button class="btn btn--primary" disabled={busy || to === props.page.path} onClick={async () => {
        const r = await run(() => api.movePage(props.page.path, to));
        if (r) { toast(`Moved. Updated links in ${r.updatedPages} page(s)${r.navUpdated ? ' and the navigation' : ''}.`); props.onMoved(); }
      }}>Move</button>
    </>}>
      <Field label="New path" hint="Links to this page from other pages, and its own relative links, are updated."><input class="input mono" value={to} autoFocus onInput={(e) => setTo((e.target as HTMLInputElement).value)} /></Field>
    </Modal>
  );
}

function BulkTagDialog(props: { pages: string[]; suggestions: string[]; onClose: () => void; onDone: () => void }) {
  const [add, setAdd] = useState<string[]>([]);
  const [removeTags, setRemoveTags] = useState<string[]>([]);
  const { busy, run } = useAction();
  return (
    <Modal title={`Tags for ${props.pages.length} page(s)`} onClose={props.onClose} actions={<>
      <button class="btn" onClick={props.onClose}>Cancel</button>
      <button class="btn btn--primary" disabled={busy || (!add.length && !removeTags.length)} onClick={async () => {
        const r = await run(() => api.assignTerms('tags', props.pages, add, removeTags));
        if (r) { toast(`Updated ${r.changed} page(s)`); props.onDone(); }
      }}>Apply</button>
    </>}>
      <Field label="Add tags"><ChipInput values={add} onChange={setAdd} suggestions={props.suggestions} /></Field>
      <Field label="Remove tags"><ChipInput values={removeTags} onChange={setRemoveTags} suggestions={props.suggestions} /></Field>
    </Modal>
  );
}

// --- Full-page editor -----------------------------------------------------------------------------

const knownKeys = ['title', 'description', 'tags', 'categories', 'date', 'draft', 'icon', 'template', 'hide'];

export function PageEditorScreen(props: { path: string }) {
  const { data, error, reload, setData } = useLoad(() => api.page(props.path), [props.path]);
  const [body, setBody] = useState('');
  const [fm, setFm] = useState<FrontMatter>({});
  const [mode, setMode] = useState<'fields' | 'raw'>('fields');
  const [raw, setRaw] = useState('');
  const [previewHtml, setPreviewHtml] = useState<string | null>(null);
  const [tags, setTags] = useState<string[]>([]);
  const [categories, setCategories] = useState<string[]>([]);
  const { busy, run } = useAction();
  const frame = useRef<HTMLIFrameElement>(null);

  useEffect(() => {
    if (!data) return;
    setBody(data.body);
    setFm(structuredClone(data.frontMatter));
    setRaw(data.raw);
  }, [data]);

  useEffect(() => {
    api.taxonomy('tags').then((t) => setTags(t.terms.map((x) => x.name))).catch(() => {});
    api.taxonomy('categories').then((t) => setCategories(t.terms.map((x) => x.name))).catch(() => {});
  }, []);

  // Live preview: the built page's own HTML, with its article replaced by the rendered draft.
  useEffect(() => {
    if (!data) return;
    const handle = setTimeout(async () => {
      try {
        const source = mode === 'raw' ? splitRaw(raw) : { fm, body };
        const { html } = await api.render(data.path, source.body, source.fm);
        setPreviewHtml(html);
      } catch { /* keep last preview */ }
    }, 400);
    return () => clearTimeout(handle);
  }, [body, fm, raw, mode, data]);

  // The preview frame shows the real built page; the draft is swapped into its article in place,
  // which keeps the scroll position and the theme's scripts working.
  const applyPreview = () => {
    const doc = frame.current?.contentDocument;
    if (!doc || previewHtml === null) return;
    const article = doc.querySelector('article.md-content__inner') ?? doc.querySelector('main');
    if (!article) return;
    article.innerHTML = previewHtml;
    const w = frame.current!.contentWindow as unknown as { document$?: { next: (d: Document) => void } };
    try { w.document$?.next(doc); } catch { /* optional */ }
  };
  useEffect(applyPreview, [previewHtml]);

  if (!data) return <Loading error={error} />;

  const dirty = mode === 'raw' ? raw !== data.raw : body !== data.body || JSON.stringify(fm) !== JSON.stringify(data.frontMatter);

  const save = async () => {
    const doc = await run(() => (mode === 'raw' ? api.saveRaw(data.path, data.hash, raw) : api.savePage(data.path, data.hash, fm, body)), 'Saved');
    if (doc) setData(doc);
  };

  const setKey = (key: string, value: Json | undefined) => {
    const next = { ...fm };
    if (value === undefined || value === '' || (Array.isArray(value) && value.length === 0)) delete next[key];
    else next[key] = value;
    setFm(next);
  };
  const str = (k: string) => (typeof fm[k] === 'string' || typeof fm[k] === 'number' ? String(fm[k]) : '');
  const list = (k: string) => (Array.isArray(fm[k]) ? (fm[k] as Json[]).map(String) : typeof fm[k] === 'string' ? [fm[k] as string] : []);

  return (
    <section class="editor-screen">
      <header class="screen-head">
        <h1 class="mono">{data.path}</h1>
        <div class="row">
          {data.url !== null && <button class="btn" onClick={() => openInPageEditor(data.path, data.url)}>✎ Edit visually</button>}
          <div class="segmented">
            <button class={mode === 'fields' ? 'on' : ''} onClick={() => { if (mode === 'raw' && raw !== data.raw && !confirmAction('Switch view and drop the unsaved raw edits?')) return; setMode('fields'); setRaw(data.raw); }}>Fields + markdown</button>
            <button class={mode === 'raw' ? 'on' : ''} onClick={() => { if (dirty && !confirmAction('Switch view and drop the unsaved edits?')) return; setMode('raw'); setBody(data.body); setFm(structuredClone(data.frontMatter)); }}>Whole file</button>
          </div>
          <button class="btn" disabled={!dirty} onClick={() => { setBody(data.body); setFm(structuredClone(data.frontMatter)); setRaw(data.raw); }}>Revert</button>
          <button class="btn btn--primary" disabled={busy || !dirty} onClick={() => void save()}>Save</button>
          <button class="btn btn--ghost" title="Reload from disk" onClick={() => { if (!dirty || confirmAction('Reload and lose unsaved edits?')) reload(); }}>⟳</button>
        </div>
      </header>
      <div class="editor-grid">
        <div class="editor-pane">
          {mode === 'fields' && (
            <details class="card" open>
              <summary>Page settings</summary>
              <div class="grid2">
                <Field label="Title"><input class="input" value={str('title')} onInput={(e) => setKey('title', (e.target as HTMLInputElement).value)} /></Field>
                <Field label="Date"><input class="input" value={str('date')} placeholder="YYYY-MM-DD" onInput={(e) => setKey('date', (e.target as HTMLInputElement).value)} disabled={typeof fm.date === 'object' && fm.date !== null} /></Field>
              </div>
              <Field label="Description"><textarea class="input" rows={2} value={str('description')} onInput={(e) => setKey('description', (e.target as HTMLTextAreaElement).value)} /></Field>
              <div class="grid2">
                <Field label="Tags"><ChipInput values={list('tags')} onChange={(v) => setKey('tags', v)} suggestions={tags} /></Field>
                <Field label="Categories"><ChipInput values={list('categories')} onChange={(v) => setKey('categories', v)} suggestions={categories} /></Field>
              </div>
              <div class="grid2">
                <Field label="Icon"><input class="input" value={str('icon')} placeholder="material/file-document" onInput={(e) => setKey('icon', (e.target as HTMLInputElement).value)} /></Field>
                <Field label="Template"><input class="input" value={str('template')} placeholder="main.html" onInput={(e) => setKey('template', (e.target as HTMLInputElement).value)} /></Field>
              </div>
              <div class="row">
                <label class="check"><input type="checkbox" checked={fm.draft === true} onChange={(e) => setKey('draft', (e.target as HTMLInputElement).checked ? true : undefined)} /> Draft</label>
                {['toc', 'nav', 'path', 'footer'].map((h) => (
                  <label class="check" key={h}><input type="checkbox" checked={list('hide').includes(h)} onChange={(e) => {
                    const cur = list('hide').filter((x) => x !== h);
                    setKey('hide', (e.target as HTMLInputElement).checked ? [...cur, h] : cur);
                  }} /> Hide {h}</label>
                ))}
              </div>
              <CustomAttributes fm={fm} onChange={setFm} />
            </details>
          )}
          <div class="editor-code">
            {mode === 'fields'
              ? <CodeEditor value={body} onChange={setBody} onSave={() => void save()} />
              : <CodeEditor value={raw} onChange={setRaw} onSave={() => void save()} />}
          </div>
        </div>
        <div class="preview-pane">
          {data.url !== null
            ? <iframe ref={frame} title="Preview" class="preview-frame" src={'/' + data.url + '?__cms=off'} onLoad={applyPreview} />
            : <div class="preview-plain md-typeset" dangerouslySetInnerHTML={{ __html: previewHtml ?? '' }} />}
        </div>
      </div>
    </section>
  );
}

function CustomAttributes(props: { fm: FrontMatter; onChange: (fm: FrontMatter) => void }) {
  const custom = Object.entries(props.fm).filter(([k]) => !knownKeys.includes(k));
  const [newKey, setNewKey] = useState('');
  const update = (key: string, text: string) => {
    let value: Json = text;
    if (/^[[{]|^(true|false|null|-?\d+(\.\d+)?)$/.test(text.trim())) { try { value = JSON.parse(text); } catch { value = text; } }
    props.onChange({ ...props.fm, [key]: value });
  };
  return (
    <div class="field">
      <span class="field__label">Custom attributes</span>
      {custom.map(([k, v]) => (
        <div class="kv" key={k}>
          <code>{k}</code>
          <input class="input mono" value={typeof v === 'string' ? v : JSON.stringify(v)} onChange={(e) => update(k, (e.target as HTMLInputElement).value)} />
          <button class="btn btn--small btn--danger" onClick={() => { const n = { ...props.fm }; delete n[k]; props.onChange(n); }}>✕</button>
        </div>
      ))}
      <div class="kv">
        <input class="input mono" placeholder="new key" value={newKey} onInput={(e) => setNewKey((e.target as HTMLInputElement).value)} />
        <button class="btn btn--small" disabled={!newKey.trim() || newKey in props.fm} onClick={() => { props.onChange({ ...props.fm, [newKey.trim()]: '' }); setNewKey(''); }}>+ Add</button>
      </div>
      <small class="muted">Values can be text or JSON (lists, numbers, true/false).</small>
    </div>
  );
}

/** Front matter and body of a raw file, for previewing the "whole file" view. */
function splitRaw(raw: string): { fm: FrontMatter; body: string } {
  const m = /^---\r?\n[\s\S]*?\r?\n(---|\.\.\.)\r?\n?/.exec(raw);
  return m ? { fm: {}, body: raw.slice(m[0].length) } : { fm: {}, body: raw };
}

export { toastError };
