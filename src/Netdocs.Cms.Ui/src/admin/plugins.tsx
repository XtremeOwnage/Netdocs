// Plugins: turn each plugin on or off, edit its options with a form (and raw JSON for the rest),
// and edit the settings some plugins keep in their own files.
import { useEffect, useMemo, useState } from 'preact/hooks';
import { api, type Abbreviation, type BlogAuthor, type FileFilterConfig, type Json, type PluginEntry } from '../shared/api';
import { openIconPicker, openImagePicker } from '../shared/pickers';
import { pluginAliases, pluginCatalog, pluginDocUrl, type OptionSpec, type PluginInfo } from './plugin-catalog';
import { navigate } from './router';
import { ChipInput, CodeEditor, Field, Loading, confirmAction, toast, useAction, useLoad } from './ui';

type Obj = Record<string, Json>;
const isObj = (v: Json | undefined): v is Obj => !!v && typeof v === 'object' && !Array.isArray(v);
const canonical = (name: string) => pluginAliases[name] ?? name;
const infoFor = (name: string): PluginInfo =>
  pluginCatalog.find((p) => p.name === canonical(name)) ?? { name, icon: '🔌', label: name, summary: 'A plugin this screen has no form for. Edit its options as JSON.', doc: '', options: [] };

export function PluginsScreen(props: { name: string | null }) {
  const { data, error, setData } = useLoad(() => api.plugins());
  const { busy, run } = useAction();
  const [filter, setFilter] = useState('');
  if (!data) return <Loading error={error} />;

  const entries = data.plugins;
  const entryFor = (name: string) => entries.find((e) => canonical(e.name) === canonical(name));
  // Every built-in plugin, plus any unknown names the config lists.
  const rows = [
    ...pluginCatalog.map((info) => ({ info, entry: entryFor(info.name) })),
    ...entries.filter((e) => !pluginCatalog.some((p) => p.name === canonical(e.name))).map((e) => ({ info: infoFor(e.name), entry: e })),
  ].filter((r) => !filter || (r.info.label + ' ' + r.info.name + ' ' + r.info.summary).toLowerCase().includes(filter.toLowerCase()));
  const selected = props.name ? rows.find((r) => r.info.name === canonical(props.name!)) ?? { info: infoFor(props.name), entry: entryFor(props.name) } : null;

  const toggle = async (info: PluginInfo, entry: PluginEntry | undefined, on: boolean) => {
    const result = await run(() => api.savePlugin(entry?.name ?? info.name, on, entry?.options ?? null), `${info.label} ${on ? 'turned on' : 'turned off'}. The site rebuilds now.`);
    if (result) setData(result);
  };

  return (
    <section>
      <header class="screen-head">
        <h1>Plugins</h1>
        <input class="input input--auto" type="search" placeholder="Filter plugins…" value={filter} onInput={(e) => setFilter((e.target as HTMLInputElement).value)} />
      </header>
      <p class="muted">Plugins are listed in <code>plugins</code> in <code>appsettings.json</code>. Turning one off keeps its options (as <code>"enabled": false</code>), so you can turn it back on later.</p>
      <div class={'plugins-layout' + (selected ? ' plugins-layout--open' : '')}>
        <ul class="plugin-list">
          {rows.map(({ info, entry }) => {
            const on = !!entry?.enabled;
            return (
              <li key={info.name} class={(selected?.info.name === info.name ? 'on ' : '') + (on ? 'plugin--on' : '')}>
                <label class="switch" title={on ? `Turn ${info.label} off` : `Turn ${info.label} on`}>
                  <input type="checkbox" checked={on} disabled={busy} onChange={(e) => toggle(info, entry, (e.target as HTMLInputElement).checked)} />
                  <span />
                </label>
                <a href={'#/plugins?name=' + encodeURIComponent(info.name)} class="plugin-list__main" title={`Open the ${info.label} settings`}>
                  <span class="plugin-icon" aria-hidden="true">{info.icon}</span>
                  <strong>{info.label}</strong>
                  {info.label !== info.name && <code class="muted"> {entry?.name ?? info.name}</code>}
                  {entry && !entry.enabled && <span class="badge">off</span>}
                  {!entry && <span class="badge">not added</span>}
                  {entry && !entry.known && <span class="badge badge--warn" title="Netdocs has no plugin by this name">unknown</span>}
                  <small class="muted">{info.summary}</small>
                </a>
                <a href={'#/plugins?name=' + encodeURIComponent(info.name)} class="plugin-list__open" title={`Open the ${info.label} settings`}>Settings ›</a>
              </li>
            );
          })}
        </ul>
        {selected && (
          <PluginEditor
            key={selected.info.name + ':' + JSON.stringify(selected.entry ?? null)}
            info={selected.info}
            entry={selected.entry}
            onSaved={setData}
          />
        )}
      </div>
    </section>
  );
}

function PluginEditor(props: { info: PluginInfo; entry: PluginEntry | undefined; onSaved: (d: { plugins: PluginEntry[] }) => void }) {
  const { info, entry } = props;
  const original = entry?.options ?? {};
  const [options, setOptions] = useState<Obj>(structuredClone(original));
  const [enabled, setEnabled] = useState(entry?.enabled ?? false);
  const known = useMemo(() => new Set(info.options.map((o) => o.group ?? o.key)), [info]);
  const rest = () => Object.fromEntries(Object.entries(options).filter(([k]) => !known.has(k)));
  const [json, setJson] = useState(() => JSON.stringify(rest(), null, 2));
  const [jsonError, setJsonError] = useState<string | null>(null);
  const { busy, run } = useAction();
  const dirty = JSON.stringify(options) !== JSON.stringify(original) || enabled !== (entry?.enabled ?? false);

  // Extra options the form does not cover, edited as JSON and merged back in.
  const applyJson = (text: string) => {
    setJson(text);
    try {
      const parsed = text.trim() ? JSON.parse(text) : {};
      if (!isObj(parsed)) throw new Error('Must be a JSON object.');
      const next: Obj = {};
      for (const [k, v] of Object.entries(options)) if (known.has(k)) next[k] = v;
      setOptions({ ...next, ...parsed });
      setJsonError(null);
    } catch (e) {
      setJsonError(e instanceof Error ? e.message : String(e));
    }
  };

  const setOption = (spec: OptionSpec, value: Json | undefined) => {
    const next = structuredClone(options);
    const target: Obj = spec.group ? (isObj(next[spec.group]) ? next[spec.group] as Obj : (next[spec.group] = {}) as Obj) : next;
    const empty = value === undefined || value === '' || (Array.isArray(value) && value.length === 0) || value === spec.default;
    if (empty) delete target[spec.key]; else target[spec.key] = value;
    if (spec.group && Object.keys(target).length === 0) delete next[spec.group];
    setOptions(next);
  };
  const getOption = (spec: OptionSpec): Json | undefined => {
    const source = spec.group ? options[spec.group] : options;
    return isObj(source as Json) ? (source as Obj)[spec.key] : undefined;
  };

  const save = async () => {
    if (jsonError) { toast('Fix the JSON options first.', 'error'); return; }
    const result = await run(() => api.savePlugin(entry?.name ?? info.name, enabled, options), `${info.label} saved. The site rebuilds now.`);
    if (result) props.onSaved(result);
  };
  const remove = async () => {
    if (!entry || !confirmAction(`Remove ${info.label} from appsettings.json? Its options are removed too. To keep them, turn it off instead.`)) return;
    const result = await run(() => api.removePlugin(entry.name), `${info.label} removed`);
    if (result) props.onSaved(result);
  };

  const groups = [...new Set(info.options.map((o) => o.group ?? ''))];
  return (
    <div class="plugin-editor">
      <div class="card">
        <header class="screen-head">
          <h2><span class="plugin-icon" aria-hidden="true">{info.icon}</span> {info.label}</h2>
          <div class="row">
            {info.doc && <a class="btn" href={pluginDocUrl(info)} target="_blank" rel="noreferrer" title="Open this plugin's documentation">Docs ↗</a>}
            <button class="btn" title="Close" onClick={() => navigate('/plugins')}>✕</button>
          </div>
        </header>
        <p>{info.summary}</p>
        <label class="check">
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled((e.target as HTMLInputElement).checked)} />
          <span><strong>Enabled</strong> <span class="muted">{!entry ? '· not in appsettings.json yet; turning it on and saving adds it' : entry.enabled ? '' : '· listed in appsettings.json but turned off'}</span></span>
        </label>

        {groups.map((group) => (
          <div key={group}>
            {group && <h3>Card layout <code class="muted">{group}</code></h3>}
            <div class="grid2">
              {info.options.filter((o) => (o.group ?? '') === group).map((spec) => (
                <OptionField key={spec.key} spec={spec} value={getOption(spec)} onChange={(v) => setOption(spec, v)} />
              ))}
            </div>
          </div>
        ))}

        <details class="plugin-json" open={info.options.length === 0 || Object.keys(rest()).length > 0}>
          <summary>{info.options.length ? 'Other options (JSON)' : 'Options (JSON)'}</summary>
          <CodeEditor value={json} onChange={applyJson} language="json" height="160px" onSave={save} />
          {jsonError && <p class="error">⚠ {jsonError}</p>}
        </details>

        <div class="row plugin-actions">
          <button class="btn btn--primary" disabled={busy || !dirty || !!jsonError} onClick={save}>Save</button>
          <button class="btn" disabled={busy || !dirty} onClick={() => { setOptions(structuredClone(original)); setEnabled(entry?.enabled ?? false); setJson(JSON.stringify(Object.fromEntries(Object.entries(original).filter(([k]) => !known.has(k))), null, 2)); setJsonError(null); }}>Revert</button>
          <span class="spacer" />
          {entry && <button class="btn btn--danger" disabled={busy} onClick={remove} title="Delete this plugin's entry from appsettings.json">Remove from config</button>}
        </div>
      </div>
      {info.extra === 'abbreviations' && <AbbreviationsEditor />}
      {info.extra === 'blog' && <AuthorsEditor />}
      {info.extra === 'file-filter' && <FileFilterEditor pluginOn={!!entry?.enabled} />}
      {info.extra === 'social' && <SocialLinksEditor />}
    </div>
  );
}

function OptionField(props: { spec: OptionSpec; value: Json | undefined; onChange: (v: Json | undefined) => void }) {
  const { spec, value } = props;
  const def = spec.default;
  const hint = [spec.hint, def !== undefined && spec.type !== 'bool' ? `Default: ${def}` : ''].filter(Boolean).join(' ');
  switch (spec.type) {
    case 'bool': {
      const checked = typeof value === 'boolean' ? value : def === true;
      return (
        <label class="check plugin-bool" title={spec.hint}>
          <input type="checkbox" checked={checked} onChange={(e) => props.onChange((e.target as HTMLInputElement).checked)} />
          <span>{spec.label}{spec.hint && <small class="muted"> {spec.hint}</small>}</span>
        </label>
      );
    }
    case 'int':
      return (
        <Field label={spec.label} hint={hint}>
          <input class="input" type="number" value={typeof value === 'number' ? value : ''} placeholder={def !== undefined ? String(def) : ''}
            onInput={(e) => { const t = (e.target as HTMLInputElement).value; props.onChange(t === '' ? undefined : Number(t)); }} />
        </Field>
      );
    case 'select':
      return (
        <Field label={spec.label} hint={spec.hint}>
          <select class="input" value={typeof value === 'string' ? value : String(def ?? '')} onChange={(e) => props.onChange((e.target as HTMLSelectElement).value)}>
            {spec.choices!.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </Field>
      );
    case 'list':
      return (
        <Field label={spec.label} hint={hint}>
          <ChipInput values={Array.isArray(value) ? value.map(String) : typeof value === 'string' ? [value] : []} onChange={(v) => props.onChange(v)} />
        </Field>
      );
    case 'map':
      return <div class="plugin-map"><MapEditor spec={spec} value={value} onChange={props.onChange} /></div>;
    case 'color': {
      const text = typeof value === 'string' ? value : '';
      return (
        <Field label={spec.label} hint={hint}>
          <span class="row color-row">
            <input type="color" value={/^#[0-9a-f]{6}$/i.test(text) ? text : (typeof def === 'string' ? def : '#9e9e9e')} onInput={(e) => props.onChange((e.target as HTMLInputElement).value)} />
            <input class="input mono" value={text} placeholder={typeof def === 'string' ? def : 'theme color'} onInput={(e) => props.onChange((e.target as HTMLInputElement).value)} />
          </span>
        </Field>
      );
    }
    default:
      return (
        <Field label={spec.label} hint={hint}>
          <input class="input" value={typeof value === 'string' ? value : value === undefined || value === null ? '' : String(value)} placeholder={def !== undefined ? String(def) : ''}
            onInput={(e) => props.onChange((e.target as HTMLInputElement).value)} />
        </Field>
      );
  }
}

/** Edits an object of string pairs, such as redirect_maps, as a two-column table. */
function MapEditor(props: { spec: OptionSpec; value: Json | undefined; onChange: (v: Json | undefined) => void }) {
  const toRows = (v: Json | undefined) => (isObj(v) ? Object.entries(v).map(([k, x]) => [k, typeof x === 'string' ? x : JSON.stringify(x)] as [string, string]) : []);
  const [rows, setRows] = useState<[string, string][]>(() => toRows(props.value));
  const [search, setSearch] = useState('');
  const [cols, holders] = [props.spec.columns ?? ['Key', 'Value'], props.spec.placeholders ?? ['', '']];
  const commit = (next: [string, string][]) => {
    setRows(next);
    const obj: Obj = {};
    for (const [k, v] of next) if (k.trim()) obj[k.trim()] = v.trim();
    props.onChange(Object.keys(obj).length ? obj : undefined);
  };
  const keys = rows.map(([k]) => k.trim()).filter(Boolean);
  const dupes = new Set(keys.filter((k, i) => keys.indexOf(k) !== i));
  const shown = rows.map((r, i) => [r, i] as const).filter(([[k, v]]) => !search || (k + ' ' + v).toLowerCase().includes(search.toLowerCase()));
  return (
    <div class="field">
      <span class="field__label row">{props.spec.label} <span class="badge">{keys.length}</span><span class="spacer" />
        {rows.length > 8 && <input class="input input--auto" type="search" placeholder="Search…" value={search} onInput={(e) => setSearch((e.target as HTMLInputElement).value)} />}
      </span>
      {props.spec.hint && <small class="muted">{props.spec.hint}</small>}
      <table class="table">
        <thead><tr><th>{cols[0]}</th><th>{cols[1]}</th><th /></tr></thead>
        <tbody>
          {shown.map(([[k, v], i]) => (
            <tr key={i}>
              <td><input class="input mono" value={k} placeholder={holders[0]} onInput={(e) => commit(rows.map((r, j) => (j === i ? [(e.target as HTMLInputElement).value, r[1]] : r)))} />
                {dupes.has(k.trim()) && <small class="error">Listed twice; the last one wins</small>}</td>
              <td><input class="input mono" value={v} placeholder={holders[1]} onInput={(e) => commit(rows.map((r, j) => (j === i ? [r[0], (e.target as HTMLInputElement).value] : r)))} /></td>
              <td class="actions"><button class="btn btn--small btn--danger" onClick={() => commit(rows.filter((_, j) => j !== i))}>Delete</button></td>
            </tr>
          ))}
          {rows.length === 0 && <tr><td colSpan={3} class="muted">None yet.</td></tr>}
        </tbody>
      </table>
      <div class="row plugin-actions"><button class="btn" onClick={() => { setSearch(''); setRows([...rows, ['', '']]); }}>+ Add</button></div>
    </div>
  );
}

/** Shows an icon by its theme name (fontawesome/brands/github, material/home…). */
const iconCache = new Map<string, Promise<string>>();
function IconPreview(props: { name: string }) {
  const [svg, setSvg] = useState('');
  useEffect(() => {
    const name = props.name.trim();
    if (!name) { setSvg(''); return; }
    let cancelled = false;
    if (!iconCache.has(name)) {
      iconCache.set(name, api.icons(name.split('/').pop() ?? name, 200)
        .then((list) => list.find((i) => i.name === name || i.name.replace(/\//g, '-') === name.replace(/\//g, '-'))?.svg ?? '')
        .catch(() => ''));
    }
    const t = setTimeout(() => void iconCache.get(name)!.then((s) => { if (!cancelled) setSvg(s); }), 250);
    return () => { cancelled = true; clearTimeout(t); };
  }, [props.name]);
  return <span class={'icon-preview' + (svg ? '' : ' icon-preview--none')} title={svg ? props.name : 'Icon not found'} dangerouslySetInnerHTML={{ __html: svg || '?' }} />;
}

// ---- abbreviations ---------------------------------------------------------------------------

interface AbbrRow { id: number; terms: string[]; definition: string }
let rowId = 0;
const groupAbbreviations = (entries: Abbreviation[]): AbbrRow[] => {
  const rows: AbbrRow[] = [];
  for (const e of entries) {
    const row = rows.find((r) => r.definition === e.definition);
    if (row) row.terms.push(e.term); else rows.push({ id: ++rowId, terms: [e.term], definition: e.definition });
  }
  return rows;
};
const flatten = (rows: AbbrRow[]): Abbreviation[] => rows.flatMap((r) => r.terms.map((term) => ({ term, definition: r.definition })));

function AbbreviationsEditor() {
  const [file, setFile] = useState<string | undefined>(undefined);
  const { data, error, setData } = useLoad(() => api.abbreviations(file), [file]);
  const [rows, setRows] = useState<AbbrRow[]>([]);
  const [search, setSearch] = useState('');
  const { busy, run } = useAction();
  useEffect(() => { if (data) setRows(groupAbbreviations(data.entries)); }, [data]);
  if (!data) return <div class="card"><Loading error={error} /></div>;

  const dirty = JSON.stringify(flatten(rows)) !== JSON.stringify(data.entries);
  const allTerms = rows.flatMap((r) => r.terms);
  const duplicates = new Set(allTerms.filter((t, i) => allTerms.indexOf(t) !== i));
  const update = (id: number, patch: Partial<AbbrRow>) => setRows(rows.map((r) => (r.id === id ? { ...r, ...patch } : r)));
  const visible = rows.filter((r) => !search || (r.terms.join(' ') + ' ' + r.definition).toLowerCase().includes(search.toLowerCase()));
  const save = async () => {
    if (duplicates.size) { toast(`Listed twice: ${[...duplicates].join(', ')}`, 'error'); return; }
    const result = await run(() => api.saveAbbreviations(data.file, flatten(rows)), 'Abbreviations saved');
    if (result) setData(result);
  };

  return (
    <div class="card">
      <header class="screen-head">
        <h3>Abbreviations <span class="badge">{allTerms.length}</span></h3>
        <div class="row">
          {data.files.length > 1 && (
            <select class="input input--auto" value={data.file} onChange={(e) => setFile((e.target as HTMLSelectElement).value)}>
              {data.files.map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          )}
          <input class="input input--auto" type="search" placeholder="Search…" value={search} onInput={(e) => setSearch((e.target as HTMLInputElement).value)} />
        </div>
      </header>
      <p class="muted">
        Saved in <code>{data.file}</code>{!data.exists && ' (created on save)'}. Put several terms on one row when they mean the same thing,
        such as <code>K8s</code> and <code>k8s</code>: each one gets the same tooltip. Terms are case sensitive.
      </p>
      <table class="table abbr-table">
        <thead><tr><th style="width:38%">Terms (synonyms)</th><th>Stands for</th><th /></tr></thead>
        <tbody>
          {visible.map((r) => (
            <tr key={r.id}>
              <td>
                <ChipInput values={r.terms} onChange={(terms) => update(r.id, { terms })} placeholder={r.terms.length ? 'Add a synonym…' : 'Term, such as HTML'} />
                {r.terms.some((t) => duplicates.has(t)) && <small class="error">Listed on another row too</small>}
              </td>
              <td><input class="input" value={r.definition} placeholder="HyperText Markup Language" onInput={(e) => update(r.id, { definition: (e.target as HTMLInputElement).value })} /></td>
              <td class="actions"><button class="btn btn--small btn--danger" title="Delete this row" onClick={() => setRows(rows.filter((x) => x.id !== r.id))}>Delete</button></td>
            </tr>
          ))}
          {visible.length === 0 && <tr><td colSpan={3} class="muted">{rows.length ? 'Nothing matches.' : 'No abbreviations yet.'}</td></tr>}
        </tbody>
      </table>
      <div class="row plugin-actions">
        <button class="btn" onClick={() => { setSearch(''); setRows([...rows, { id: ++rowId, terms: [], definition: '' }]); }}>+ Add abbreviation</button>
        <span class="spacer" />
        <button class="btn" disabled={!dirty || busy} onClick={() => setRows(groupAbbreviations(data.entries))}>Revert</button>
        <button class="btn btn--primary" disabled={!dirty || busy} onClick={save}>Save abbreviations</button>
      </div>
    </div>
  );
}

// ---- blog authors ----------------------------------------------------------------------------

function AuthorsEditor() {
  const { data, error, setData } = useLoad(() => api.pluginAuthors());
  const [authors, setAuthors] = useState<BlogAuthor[]>([]);
  const { busy, run } = useAction();
  useEffect(() => { if (data) setAuthors(structuredClone(data.authors)); }, [data]);
  if (!data) return <div class="card"><Loading error={error} /></div>;
  const dirty = JSON.stringify(authors) !== JSON.stringify(data.authors);
  const update = (i: number, patch: Partial<BlogAuthor>) => setAuthors(authors.map((a, j) => (j === i ? { ...a, ...patch } : a)));
  const save = async () => {
    const result = await run(() => api.savePluginAuthors(authors), 'Authors saved');
    if (result) setData(result);
  };
  return (
    <div class="card">
      <h3>Authors</h3>
      <p class="muted">Saved in <code>{data.file}</code> in the docs folder. Posts name authors by id in their <code>authors</code> front matter. Saving rewrites the file, so comments in it are not kept.</p>
      <table class="table">
        <thead><tr><th>Id</th><th>Name</th><th>Description</th><th>Avatar</th><th /></tr></thead>
        <tbody>
          {authors.map((a, i) => (
            <tr key={i}>
              <td><input class="input mono" value={a.id} placeholder="jdoe" onInput={(e) => update(i, { id: (e.target as HTMLInputElement).value })} /></td>
              <td><input class="input" value={a.name} onInput={(e) => update(i, { name: (e.target as HTMLInputElement).value })} /></td>
              <td><input class="input" value={a.description} placeholder="Maintainer" onInput={(e) => update(i, { description: (e.target as HTMLInputElement).value })} /></td>
              <td>
                <span class="row nowrap">
                  {a.avatar && !/^https?:/.test(a.avatar) && <img class="avatar-thumb" src={api.mediaUrl(a.avatar.replace(/^\//, ''))} alt="" />}
                  <input class="input mono" value={a.avatar} placeholder="URL or image path" onInput={(e) => update(i, { avatar: (e.target as HTMLInputElement).value })} />
                  <button class="btn btn--small" title="Pick an image from the media library" onClick={async () => { const p = await openImagePicker(''); if (p) update(i, { avatar: p.path }); }}>…</button>
                </span>
              </td>
              <td class="actions"><button class="btn btn--small btn--danger" onClick={() => setAuthors(authors.filter((_, j) => j !== i))}>Delete</button></td>
            </tr>
          ))}
          {authors.length === 0 && <tr><td colSpan={5} class="muted">No authors yet.</td></tr>}
        </tbody>
      </table>
      <div class="row plugin-actions">
        <button class="btn" onClick={() => setAuthors([...authors, { id: '', name: '', description: '', avatar: '' }])}>+ Add author</button>
        <span class="spacer" />
        <button class="btn" disabled={!dirty || busy} onClick={() => setAuthors(structuredClone(data.authors))}>Revert</button>
        <button class="btn btn--primary" disabled={!dirty || busy} onClick={save}>Save authors</button>
      </div>
    </div>
  );
}

// ---- file filter -----------------------------------------------------------------------------

function FileFilterEditor(props: { pluginOn: boolean }) {
  const { data, error, setData } = useLoad(() => api.fileFilter());
  const [fields, setFields] = useState<Obj>({});
  const [yaml, setYaml] = useState('');
  const [ignore, setIgnore] = useState('');
  const [raw, setRaw] = useState(false);
  const { busy, run } = useAction();
  useEffect(() => {
    if (!data) return;
    setFields(structuredClone(data.fields)); setYaml(data.yaml); setIgnore(data.ignoreText);
    if (/!ENV/.test(data.yaml)) setRaw(true);
  }, [data]);
  if (!data) return <div class="card"><Loading error={error} /></div>;

  const set = (k: string, v: Json) => setFields({ ...fields, [k]: v });
  const list = (k: string) => (Array.isArray(fields[k]) ? (fields[k] as Json[]).map(String) : []);
  const dirty = raw ? yaml !== data.yaml : JSON.stringify(fields) !== JSON.stringify(data.fields) || !data.exists;
  const ignoreDirty = ignore !== data.ignoreText;
  const save = async () => {
    const body: { yaml?: string; fields?: Obj; ignoreText?: string } = {};
    if (dirty) { if (raw) body.yaml = yaml; else body.fields = fields; }
    if (ignoreDirty) body.ignoreText = ignore;
    const result = await run(() => api.saveFileFilter(body), 'File filter saved');
    if (result) setData(result as FileFilterConfig);
  };

  return (
    <div class="card">
      <header class="screen-head">
        <h3>Filter settings <code class="muted">.file-filter.yml</code></h3>
        <label class="check"><input type="checkbox" checked={raw} onChange={(e) => setRaw((e.target as HTMLInputElement).checked)} /><span>Edit as YAML</span></label>
      </header>
      {!props.pluginOn && <p class="card--hint muted">The label lists below only apply while the File filter plugin is on. The ignored paths apply either way.</p>}
      {!data.exists && <p class="muted">There is no <code>.file-filter.yml</code> yet. Saving creates one in the project folder.</p>}
      {raw ? (
        <>
          {/!ENV/.test(data.yaml) && <p class="muted">This file reads environment variables (<code>!ENV</code>), so it opens as YAML to keep them.</p>}
          <CodeEditor value={yaml} onChange={setYaml} height="180px" onSave={save} />
        </>
      ) : (
        <>
          <div class="grid2">
            <label class="check"><input type="checkbox" checked={fields.enabled !== false} onChange={(e) => set('enabled', (e.target as HTMLInputElement).checked)} /><span>Filter on build</span></label>
            <label class="check"><input type="checkbox" checked={fields.enabled_on_serve !== false} onChange={(e) => set('enabled_on_serve', (e.target as HTMLInputElement).checked)} /><span>Also filter while serving <small class="muted">Off shows everything in netdocs serve.</small></span></label>
            <label class="check"><input type="checkbox" checked={fields.mkdocsignore !== false} onChange={(e) => set('mkdocsignore', (e.target as HTMLInputElement).checked)} /><span>Use the ignore file</span></label>
          </div>
          <div class="grid2">
            <Field label="Hide pages labelled" hint="Pages whose labels include any of these are left out."><ChipInput values={list('exclude_tag')} onChange={(v) => set('exclude_tag', v)} placeholder="draft, internal…" /></Field>
            <Field label="Always keep pages labelled" hint="Wins over the hide list."><ChipInput values={list('include_tag')} onChange={(v) => set('include_tag', v)} /></Field>
            <Field label="Label field" hint="Front-matter key that holds a page's labels. Default: labels"><input class="input mono" value={String(fields.metadata_property ?? '')} placeholder="labels" onInput={(e) => set('metadata_property', (e.target as HTMLInputElement).value)} /></Field>
            <Field label="Ignore file" hint="Default: .mkdocsignore"><input class="input mono" value={String(fields.mkdocsignore_file ?? '')} placeholder=".mkdocsignore" onInput={(e) => set('mkdocsignore_file', (e.target as HTMLInputElement).value)} /></Field>
          </div>
        </>
      )}
      <h3>Ignored paths <code class="muted">{data.ignoreFile}</code></h3>
      <p class="muted">One path or glob per line, relative to the docs folder, like a <code>.gitignore</code>.</p>
      <textarea class="input mono" rows={6} value={ignore} placeholder={'drafts/\ninternal/*.md'} onInput={(e) => setIgnore((e.target as HTMLTextAreaElement).value)} />
      <div class="row plugin-actions">
        <span class="spacer" />
        <button class="btn" disabled={(!dirty && !ignoreDirty) || busy} onClick={() => { setFields(structuredClone(data.fields)); setYaml(data.yaml); setIgnore(data.ignoreText); }}>Revert</button>
        <button class="btn btn--primary" disabled={(!dirty && !ignoreDirty) || busy} onClick={save}>Save filter settings</button>
      </div>
    </div>
  );
}

// ---- footer social links ---------------------------------------------------------------------

function SocialLinksEditor() {
  const { data, error, setData } = useLoad(() => api.socialLinks());
  const [links, setLinks] = useState<Obj[]>([]);
  const { busy, run } = useAction();
  useEffect(() => { if (data) setLinks(structuredClone(data.links)); }, [data]);
  if (!data) return <div class="card"><Loading error={error} /></div>;
  const dirty = JSON.stringify(links) !== JSON.stringify(data.links);
  const update = (i: number, patch: Obj) => setLinks(links.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const move = (i: number, d: number) => { const next = [...links]; const [x] = next.splice(i, 1); next.splice(i + d, 0, x); setLinks(next); };
  const save = async () => {
    const result = await run(() => api.saveSocialLinks(links), 'Social links saved');
    if (result) setData(result);
  };
  return (
    <div class="card">
      <h3>Footer social links <code class="muted">extra.social</code></h3>
      <p class="muted">The icons in the page footer. These are part of the theme rather than the social-cards plugin, but they are often set up together.</p>
      <table class="table">
        <thead><tr><th>Icon</th><th>Link</th><th>Tooltip</th><th /></tr></thead>
        <tbody>
          {links.map((l, i) => (
            <tr key={i}>
              <td>
                <span class="row nowrap">
                  <IconPreview name={String(l.icon ?? '')} />
                  <input class="input mono" value={String(l.icon ?? '')} placeholder="fontawesome/brands/github" onInput={(e) => update(i, { icon: (e.target as HTMLInputElement).value })} />
                  <button class="btn btn--small" title="Choose an icon" onClick={async () => { const icon = await openIconPicker(String(l.icon ?? '')); if (icon) update(i, { icon }); }}>…</button>
                </span>
              </td>
              <td><input class="input mono" value={String(l.link ?? '')} placeholder="https://…" onInput={(e) => update(i, { link: (e.target as HTMLInputElement).value })} /></td>
              <td><input class="input" value={String(l.name ?? '')} placeholder="Optional" onInput={(e) => update(i, { name: (e.target as HTMLInputElement).value })} /></td>
              <td class="actions">
                <button class="btn btn--small" disabled={i === 0} title="Move up" onClick={() => move(i, -1)}>↑</button>
                <button class="btn btn--small" disabled={i === links.length - 1} title="Move down" onClick={() => move(i, 1)}>↓</button>
                <button class="btn btn--small btn--danger" onClick={() => setLinks(links.filter((_, j) => j !== i))}>Delete</button>
              </td>
            </tr>
          ))}
          {links.length === 0 && <tr><td colSpan={4} class="muted">No footer links.</td></tr>}
        </tbody>
      </table>
      <div class="row plugin-actions">
        <button class="btn" onClick={() => setLinks([...links, { icon: '', link: '' }])}>+ Add link</button>
        <span class="spacer" />
        <button class="btn" disabled={!dirty || busy} onClick={() => setLinks(structuredClone(data.links))}>Revert</button>
        <button class="btn btn--primary" disabled={!dirty || busy} onClick={save}>Save links</button>
      </div>
    </div>
  );
}
