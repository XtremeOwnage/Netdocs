// Media library: browse, upload, rename, delete and optimize images.
import { useMemo, useRef, useState } from 'preact/hooks';
import { api, formatBytes, type MediaItem, type WebpResult } from '../shared/api';
import { Field, Loading, Modal, confirmAction, toast, toastError, useAction, useLoad } from './ui';

export function MediaScreen() {
  const { data, error, reload } = useLoad(() => api.media());
  const [folder, setFolder] = useState('');
  const [filter, setFilter] = useState('');
  const [kind, setKind] = useState<'all' | 'image' | 'file' | 'unused' | 'convertible'>('all');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [detail, setDetail] = useState<MediaItem | null>(null);
  const [optimize, setOptimize] = useState<string[] | null>(null);
  const [uploadFolder, setUploadFolder] = useState('');
  const [dragging, setDragging] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const { busy, run } = useAction();

  const folders = useMemo(() => [...new Set((data ?? []).map((m) => m.path.includes('/') ? m.path.slice(0, m.path.lastIndexOf('/')) : ''))].sort(), [data]);
  const convertible = (m: MediaItem) => /\.(png|jpe?g|gif|bmp)$/i.test(m.path);
  const rows = useMemo(() => (data ?? []).filter((m) => {
    const dir = m.path.includes('/') ? m.path.slice(0, m.path.lastIndexOf('/')) : '';
    return (!folder || dir === folder || dir.startsWith(folder + '/'))
      && (!filter || m.path.toLowerCase().includes(filter.toLowerCase()))
      && (kind === 'all' || (kind === 'unused' ? m.references === 0 : kind === 'convertible' ? convertible(m) : m.kind === kind));
  }), [data, folder, filter, kind]);

  if (!data) return <Loading error={error} />;
  const totalSize = data.reduce((n, m) => n + m.size, 0);
  const convertibleCount = data.filter(convertible).length;

  const upload = async (files: File[]) => {
    if (!files.length) return;
    const r = await run(() => api.upload(uploadFolder || folder, files, false));
    if (r) { toast(`Uploaded ${r.saved.length} file(s)`); reload(); }
  };

  const toggle = (p: string) => {
    const next = new Set(selected);
    if (next.has(p)) next.delete(p); else next.add(p);
    setSelected(next);
  };

  return (
    <section
      onDragOver={(e) => { e.preventDefault(); setDragging(true); }}
      onDragLeave={(e) => { if (e.target === e.currentTarget) setDragging(false); }}
      onDrop={(e) => { e.preventDefault(); setDragging(false); void upload([...(e.dataTransfer?.files ?? [])]); }}
      class={dragging ? 'dropzone dropzone--active' : 'dropzone'}>
      <header class="screen-head">
        <h1>Media <span class="muted">({data.length} files, {formatBytes(totalSize)})</span></h1>
        <div class="row">
          {convertibleCount > 0 && <button class="btn" onClick={() => setOptimize(data.filter(convertible).map((m) => m.path))}>Optimize all {convertibleCount} PNG/JPEG…</button>}
          <input class="input input--auto mono" placeholder="upload folder (e.g. images)" value={uploadFolder} onInput={(e) => setUploadFolder((e.target as HTMLInputElement).value)} list="media-folders" />
          <datalist id="media-folders">{folders.map((f) => <option key={f} value={f} />)}</datalist>
          <button class="btn btn--primary" disabled={busy} onClick={() => fileInput.current?.click()}>⬆ Upload</button>
          <input ref={fileInput} type="file" multiple hidden onChange={(e) => { void upload([...((e.target as HTMLInputElement).files ?? [])]); (e.target as HTMLInputElement).value = ''; }} />
        </div>
      </header>
      <p class="muted small">Drop files anywhere on this screen to upload them.</p>
      <div class="toolbar">
        <select class="input input--auto" value={folder} onChange={(e) => setFolder((e.target as HTMLSelectElement).value)}>
          <option value="">All folders</option>
          {folders.filter(Boolean).map((f) => <option key={f} value={f}>{f}</option>)}
        </select>
        <select class="input input--auto" value={kind} onChange={(e) => setKind((e.target as HTMLSelectElement).value as typeof kind)}>
          <option value="all">Everything</option>
          <option value="image">Images</option>
          <option value="file">Other files</option>
          <option value="convertible">Can convert to WebP</option>
          <option value="unused">Not used by any page</option>
        </select>
        <input class="input" type="search" placeholder="Filter…" value={filter} onInput={(e) => setFilter((e.target as HTMLInputElement).value)} />
        {selected.size > 0 && <>
          <button class="btn" onClick={() => setOptimize([...selected].filter((p) => /\.(png|jpe?g|gif|bmp)$/i.test(p)))}>Convert {selected.size} to WebP…</button>
          <button class="btn btn--danger" onClick={async () => {
            if (!confirmAction(`Delete ${selected.size} file(s)? Files still used by pages are skipped.`)) return;
            let n = 0;
            for (const p of selected) { try { await api.deleteMedia(p, false); n++; } catch (e) { toastError(e); } }
            toast(`Deleted ${n} file(s)`);
            setSelected(new Set());
            reload();
          }}>Delete {selected.size}</button>
        </>}
      </div>
      <div class="media-grid">
        {rows.map((m) => (
          <figure key={m.path} class={`media-item${selected.has(m.path) ? ' media-item--selected' : ''}`}>
            <input type="checkbox" class="media-item__check" checked={selected.has(m.path)} onChange={() => toggle(m.path)} aria-label={`Select ${m.path}`} />
            <button class="media-item__thumb" onClick={() => setDetail(m)} title={m.path}>
              {m.kind === 'image' ? <img src={api.mediaUrl(m.path) + `&v=${encodeURIComponent(m.modified)}`} alt="" loading="lazy" /> : <span class="media-item__ext">{m.path.split('.').pop()}</span>}
            </button>
            <figcaption>
              <span class="mono small ellipsis" title={m.path}>{m.path.split('/').pop()}</span>
              <span class="muted small">{formatBytes(m.size)}{m.width ? ` · ${m.width}×${m.height}` : ''}{m.references === 0 ? ' · unused' : ''}</span>
            </figcaption>
          </figure>
        ))}
      </div>
      {rows.length === 0 && <p class="muted">No files match.</p>}
      {detail && <MediaDetail item={detail} onClose={() => setDetail(null)} onChanged={() => { setDetail(null); reload(); }} onOptimize={() => { setOptimize([detail.path]); setDetail(null); }} />}
      {optimize && <OptimizeDialog paths={optimize} onClose={() => setOptimize(null)} onDone={() => { setOptimize(null); setSelected(new Set()); reload(); }} />}
    </section>
  );
}

function MediaDetail(props: { item: MediaItem; onClose: () => void; onChanged: () => void; onOptimize: () => void }) {
  const m = props.item;
  const [to, setTo] = useState(m.path);
  const [maxWidth, setMaxWidth] = useState(1600);
  const refs = useLoad(() => api.references(m.path), [m.path]);
  const { busy, run } = useAction();
  const name = m.path.split('/').pop()!;
  const snippet = m.kind === 'image' ? `![${name.replace(/\.[^.]+$/, '')}](${m.path})` : `[${name}](${m.path})`;
  return (
    <Modal title={name} onClose={props.onClose} wide>
      <div class="media-detail">
        <div class="media-detail__preview">
          {m.kind === 'image' ? <img src={api.mediaUrl(m.path)} alt="" /> : <a href={api.mediaUrl(m.path)} target="_blank" rel="noopener">Open file</a>}
        </div>
        <div>
          <p class="mono">{m.path}</p>
          <p class="muted">{formatBytes(m.size)}{m.width ? ` · ${m.width}×${m.height}px` : ''}</p>
          <Field label="Markdown" hint="Paths are relative to the docs folder; adjust for the page you paste into.">
            <div class="row"><input class="input mono" readOnly value={snippet} /><button class="btn" onClick={() => { void navigator.clipboard?.writeText(snippet); toast('Copied'); }}>Copy</button></div>
          </Field>
          <Field label="Used by">
            {refs.data ? (refs.data.length ? <ul class="plain">{refs.data.map((p) => <li key={p}><a class="mono" href={`#/pages/edit?path=${encodeURIComponent(p)}`}>{p}</a></li>)}</ul> : <span class="muted">No pages link to this file.</span>) : <Loading error={refs.error} />}
          </Field>
          <Field label="Rename or move" hint="Pages that link to the file are updated.">
            <div class="row">
              <input class="input mono" value={to} onInput={(e) => setTo((e.target as HTMLInputElement).value)} />
              <button class="btn" disabled={busy || to === m.path} onClick={async () => { const r = await run(() => api.moveMedia(m.path, to)); if (r) { toast(`Moved. Updated ${r.updatedPages} page(s).`); props.onChanged(); } }}>Move</button>
            </div>
          </Field>
          {m.kind === 'image' && !m.path.endsWith('.svg') && (
            <Field label="Optimize">
              <div class="row">
                {/\.(png|jpe?g|gif|bmp)$/i.test(m.path) && <button class="btn" onClick={props.onOptimize}>Convert to WebP…</button>}
                <input class="input input--narrow" type="number" min={64} value={maxWidth} onInput={(e) => setMaxWidth(+(e.target as HTMLInputElement).value)} />
                <button class="btn" disabled={busy || !m.width || m.width <= maxWidth} onClick={async () => { const r = await run(() => api.resize(m.path, maxWidth)); if (r) { toast(`Resized to ${formatBytes(r.size)}`); props.onChanged(); } }}>Shrink to this width</button>
              </div>
            </Field>
          )}
          <button class="btn btn--danger" disabled={busy} onClick={async () => {
            const used = refs.data?.length ?? 0;
            if (!confirmAction(used ? `${used} page(s) still use this file. Delete it anyway? Their links will break.` : `Delete ${m.path}?`)) return;
            const r = await run(() => api.deleteMedia(m.path, used > 0).then(() => true), 'Deleted');
            if (r) props.onChanged();
          }}>Delete</button>
        </div>
      </div>
    </Modal>
  );
}

function OptimizeDialog(props: { paths: string[]; onClose: () => void; onDone: () => void }) {
  const [quality, setQuality] = useState(80);
  const [limit, setLimit] = useState(false);
  const [maxWidth, setMaxWidth] = useState(1920);
  const [results, setResults] = useState<WebpResult[] | null>(null);
  const { busy, run } = useAction();
  if (results) {
    const ok = results.filter((r) => !r.error);
    const saved = ok.reduce((n, r) => n + (r.oldSize - r.newSize), 0);
    return (
      <Modal title="WebP conversion finished" onClose={props.onDone} actions={<button class="btn btn--primary" onClick={props.onDone}>Done</button>}>
        <p>Converted {ok.length} of {results.length} image(s), saving {formatBytes(Math.max(0, saved))}. Links in {ok.reduce((n, r) => n + r.updatedPages, 0)} page(s) now point at the new files, and the originals were deleted.</p>
        <table class="table"><tbody>{results.map((r) => (
          <tr key={r.from}><td class="mono small">{r.from}</td><td>{r.error ? <span class="error">{r.error}</span> : `${formatBytes(r.oldSize)} → ${formatBytes(r.newSize)}`}</td></tr>
        ))}</tbody></table>
      </Modal>
    );
  }
  return (
    <Modal title={`Convert ${props.paths.length} image(s) to WebP`} onClose={props.onClose} actions={<>
      <button class="btn" onClick={props.onClose}>Cancel</button>
      <button class="btn btn--primary" disabled={busy || props.paths.length === 0} onClick={async () => {
        const r = await run(() => api.webp(props.paths, quality, limit ? maxWidth : null));
        if (r) setResults(r);
      }}>{busy ? 'Converting…' : 'Convert'}</button>
    </>}>
      <p>Each image is saved as <code>.webp</code> next to the original, every page that links to it is updated, and the original file is deleted, so only the optimized image ends up in source control.</p>
      <Field label={`Quality: ${quality}`}><input type="range" min={40} max={100} value={quality} onInput={(e) => setQuality(+(e.target as HTMLInputElement).value)} /></Field>
      <label class="check"><input type="checkbox" checked={limit} onChange={() => setLimit(!limit)} /> Also shrink images wider than</label>
      <input class="input input--narrow" type="number" min={64} value={maxWidth} disabled={!limit} onInput={(e) => setMaxWidth(+(e.target as HTMLInputElement).value)} /> px
      <details><summary class="muted">{props.paths.length} file(s)</summary><ul class="plain mono small">{props.paths.map((p) => <li key={p}>{p}</li>)}</ul></details>
    </Modal>
  );
}
