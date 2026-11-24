// Tags and categories: usage, rename, merge, delete, and clean-up suggestions.
import { Fragment } from 'preact';
import { useMemo, useState } from 'preact/hooks';
import { api, type TermUsage } from '../shared/api';
import { Field, Loading, Modal, confirmAction, toast, useAction, useLoad } from './ui';

export function TaxonomyScreen(props: { field: string }) {
  const field = props.field === 'categories' ? 'categories' : 'tags';
  const { data, error, reload } = useLoad(() => api.taxonomy(field), [field]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [filter, setFilter] = useState('');
  const [rename, setRename] = useState<string[] | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const { run } = useAction();

  const rows = useMemo(() => (data?.terms ?? []).filter((t) => !filter || t.name.toLowerCase().includes(filter.toLowerCase())), [data, filter]);
  if (!data) return <Loading error={error} />;
  const noun = field === 'tags' ? 'tag' : 'category';

  const toggle = (name: string) => {
    const next = new Set(selected);
    if (next.has(name)) next.delete(name); else next.add(name);
    setSelected(next);
  };

  const remove = async (names: string[]) => {
    const pages = data.terms.filter((t) => names.includes(t.name)).reduce((n, t) => n + t.count, 0);
    if (!confirmAction(`Remove ${names.length === 1 ? `"${names[0]}"` : `${names.length} ${noun}s`} from ${pages} page(s)?`)) return;
    const r = await run(() => api.deleteTerms(field, names));
    if (r) { toast(`Updated ${r.changed} page(s)`); setSelected(new Set()); reload(); }
  };

  return (
    <section>
      <header class="screen-head">
        <h1>{field === 'tags' ? 'Tags' : 'Categories'} <span class="muted">({data.terms.length})</span></h1>
        <div class="segmented">
          <a class={field === 'tags' ? 'on' : ''} href="#/taxonomy?field=tags">Tags</a>
          <a class={field === 'categories' ? 'on' : ''} href="#/taxonomy?field=categories">Categories</a>
        </div>
      </header>

      {(data.similar.length > 0 || data.singleUse.length > 0) && (
        <div class="card card--hint">
          <h3>Clean-up suggestions</h3>
          {data.similar.length > 0 && <>
            <p>These look like the same {noun} spelled differently. Merging rewrites every page that uses them.</p>
            <ul class="plain">
              {data.similar.map((s) => (
                <li key={s.names.join('|')}>
                  {s.names.map((n) => <span class="tag" key={n}>{n} <span class="muted">{data.terms.find((t) => t.name === n)?.count}</span></span>)}
                  <span class="muted small"> {s.reason}</span>{' '}
                  <button class="btn btn--small" onClick={() => setRename(s.names)}>Merge…</button>
                </li>
              ))}
            </ul>
          </>}
          {data.singleUse.length > 0 && <p class="muted">Used on only one page: {data.singleUse.slice(0, 40).map((n) => <span class="tag" key={n}>{n}</span>)}{data.singleUse.length > 40 && ` and ${data.singleUse.length - 40} more`}</p>}
        </div>
      )}

      <div class="toolbar">
        <input class="input" type="search" placeholder={`Filter ${noun}s…`} value={filter} onInput={(e) => setFilter((e.target as HTMLInputElement).value)} />
        {selected.size > 0 && <>
          <button class="btn" onClick={() => setRename([...selected])}>{selected.size > 1 ? `Merge ${selected.size}…` : 'Rename…'}</button>
          <button class="btn btn--danger" onClick={() => void remove([...selected])}>Delete {selected.size}</button>
        </>}
      </div>

      <table class="table">
        <thead><tr><th /><th>{noun[0].toUpperCase() + noun.slice(1)}</th><th>Pages</th><th /></tr></thead>
        <tbody>
          {rows.map((t: TermUsage) => (
            <Fragment key={t.name}>
              <tr>
                <td><input type="checkbox" checked={selected.has(t.name)} onChange={() => toggle(t.name)} aria-label={`Select ${t.name}`} /></td>
                <td><span class="tag">{t.name}</span></td>
                <td><button class="btn btn--link" onClick={() => setExpanded(expanded === t.name ? null : t.name)}>{t.count} page{t.count === 1 ? '' : 's'}</button></td>
                <td class="actions">
                  <button class="btn btn--small" onClick={() => setRename([t.name])}>Rename</button>
                  <button class="btn btn--small btn--danger" onClick={() => void remove([t.name])}>Delete</button>
                </td>
              </tr>
              {expanded === t.name && <tr class="subrow"><td /><td colSpan={3}>{t.pages.map((p) => <div key={p}><a class="mono" href={`#/pages/edit?path=${encodeURIComponent(p)}`}>{p}</a></div>)}</td></tr>}
            </Fragment>
          ))}
        </tbody>
      </table>
      {data.terms.length === 0 && <p class="muted">No pages use {field} in their front matter yet.</p>}

      {rename && <RenameDialog field={field} names={rename} existing={data.terms.map((t) => t.name)} onClose={() => setRename(null)} onDone={() => { setRename(null); setSelected(new Set()); reload(); }} />}
    </section>
  );
}

function RenameDialog(props: { field: string; names: string[]; existing: string[]; onClose: () => void; onDone: () => void }) {
  const [to, setTo] = useState(props.names[0]);
  const { busy, run } = useAction();
  const merge = props.names.length > 1;
  return (
    <Modal title={merge ? `Merge ${props.names.length} into one` : `Rename "${props.names[0]}"`} onClose={props.onClose} actions={<>
      <button class="btn" onClick={props.onClose}>Cancel</button>
      <button class="btn btn--primary" disabled={busy || !to.trim()} onClick={async () => {
        const r = await run(() => api.renameTerms(props.field, props.names, to.trim()));
        if (r) { toast(`Updated ${r.changed} page(s)`); props.onDone(); }
      }}>{merge ? 'Merge' : 'Rename'}</button>
    </>}>
      {merge && <p>Every page using {props.names.map((n) => <span class="tag" key={n}>{n}</span>)} will use the name below instead.</p>}
      {merge && <div class="row">{props.names.map((n) => <button class="btn btn--small" key={n} onClick={() => setTo(n)}>Use "{n}"</button>)}</div>}
      <Field label="New name"><input class="input" value={to} list="rename-existing" autoFocus onInput={(e) => setTo((e.target as HTMLInputElement).value)} /></Field>
      <datalist id="rename-existing">{props.existing.map((n) => <option value={n} key={n} />)}</datalist>
    </Modal>
  );
}
