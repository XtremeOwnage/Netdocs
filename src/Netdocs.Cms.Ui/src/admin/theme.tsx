// Theme settings, template overrides, site settings and the raw appsettings.json editor.
import { useEffect, useRef, useState } from 'preact/hooks';
import { featureDocUrl, featureHelp } from './features';
import { api, type Json, type SiteInfo, type TemplateInfo } from '../shared/api';
import { CodeEditor, Diff, Field, Loading, Modal, confirmAction, toast, useAction, useLoad } from './ui';

type Obj = Record<string, Json>;
const obj = (v: Json | undefined): Obj => (v && typeof v === 'object' && !Array.isArray(v) ? v as Obj : {});
const str = (v: Json | undefined) => (typeof v === 'string' ? v : '');

const viewports = [
  { id: 'fit', label: 'Fit pane', width: null },
  { id: 'mobile', label: 'Mobile · 390px', width: 390 },
  { id: 'tablet', label: 'Tablet · 820px', width: 820 },
  { id: 'laptop', label: 'Laptop · 1366px', width: 1366 },
  { id: 'desktop', label: 'Desktop · 1920px', width: 1920 },
  { id: 'wide', label: 'Extra wide · 2560px', width: 2560 },
];

/** An iframe rendered at a chosen viewport width and scaled down to fit its pane. */
function ScaledFrame(props: { src: string; width: number | null; onLoad: () => void; status: string | null }) {
  const box = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ w: 0, h: 0 });
  useEffect(() => {
    if (!box.current) return;
    const ro = new ResizeObserver(([e]) => setSize({ w: e.contentRect.width, h: e.contentRect.height }));
    ro.observe(box.current);
    return () => ro.disconnect();
  }, []);
  const scale = props.width && size.w ? Math.min(1, size.w / props.width) : 1;
  const style = props.width
    ? { width: props.width + 'px', height: (size.h / scale) + 'px', transform: `scale(${scale})`, transformOrigin: '0 0' }
    : { width: '100%', height: '100%' };
  return (
    <div class="preview-box" ref={box}>
      <iframe class="preview-frame" title="Site preview" src={props.src} style={style} onLoad={props.onLoad} />
      {props.width && <span class="preview-scale">{props.width}px{scale < 1 ? ` · shown at ${Math.round(scale * 100)}%` : ''}</span>}
      {props.status && <div class="preview-status"><span class="spinner" /> {props.status}</div>}
    </div>
  );
}

export function ThemeScreen(props: { site: SiteInfo }) {
  const { data, error, reload } = useLoad(() => api.settings());
  const [settings, setSettings] = useState<Obj | null>(null);
  const { busy, run } = useAction();
  useEffect(() => { if (data) setSettings(structuredClone(data)); }, [data]);
  if (!data || !settings) return <Loading error={error} />;

  const theme = obj(settings.theme);
  const setTheme = (key: string, value: Json | undefined) => {
    const next = { ...theme };
    if (value === undefined || value === '') delete next[key]; else next[key] = value;
    setSettings({ ...settings, theme: next });
  };
  const features = Array.isArray(theme.features) ? (theme.features as string[]) : [];
  const palettes = Array.isArray(theme.palette) ? (theme.palette as Obj[]) : [];
  const font = obj(theme.font);
  const dirty = JSON.stringify(settings) !== JSON.stringify(data);
  const setPalette = (i: number, patch: Obj) => setTheme('palette', palettes.map((p, j) => (j === i ? { ...p, ...patch } : p)));

  return (
    <section>
      <header class="screen-head">
        <h1>Theme</h1>
        <div class="row">
          <a class="btn" href="#/templates">Templates…</a>
          <button class="btn" disabled={!dirty} onClick={() => setSettings(structuredClone(data))}>Revert</button>
          <button class="btn btn--primary" disabled={!dirty || busy} onClick={async () => { const r = await run(() => api.saveSettings(settings), 'Theme saved'); if (r) reload(); }}>Save</button>
        </div>
      </header>
      <div class="grid2">
        <div class="card">
          <h3>Look</h3>
          <div class="grid2">
            <Field label="Language"><input class="input" value={str(theme.language)} placeholder="en" onInput={(e) => setTheme('language', (e.target as HTMLInputElement).value)} /></Field>
            <Field label="Code highlighting"><select class="input" value={str(theme.highlight) || 'highlightjs'} onChange={(e) => setTheme('highlight', (e.target as HTMLSelectElement).value)}><option value="highlightjs">highlight.js</option><option value="none">none</option><option value="custom">custom</option></select></Field>
            <Field label="Text font"><input class="input" value={str(font.text)} placeholder="Roboto" onInput={(e) => setTheme('font', { ...font, text: (e.target as HTMLInputElement).value })} /></Field>
            <Field label="Code font"><input class="input" value={str(font.code)} placeholder="Roboto Mono" onInput={(e) => setTheme('font', { ...font, code: (e.target as HTMLInputElement).value })} /></Field>
            <Field label="Logo" hint="Path in the docs folder, or an icon name"><input class="input mono" value={str(theme.logo)} onInput={(e) => setTheme('logo', (e.target as HTMLInputElement).value)} /></Field>
            <Field label="Favicon"><input class="input mono" value={str(theme.favicon)} onInput={(e) => setTheme('favicon', (e.target as HTMLInputElement).value)} /></Field>
            <Field label="Template overrides folder" hint="customDir: templates here replace the theme's"><input class="input mono" value={str(theme.customDir)} placeholder="overrides" onInput={(e) => setTheme('customDir', (e.target as HTMLInputElement).value)} /></Field>
          </div>
          <h3>Color palettes</h3>
          {palettes.map((p, i) => (
            <fieldset class="palette" key={i}>
              <legend>Palette {i + 1}</legend>
              <div class="grid3">
                <Field label="Scheme"><select class="input" value={str(p.scheme) || 'default'} onChange={(e) => setPalette(i, { scheme: (e.target as HTMLSelectElement).value })}><option value="default">default (light)</option><option value="slate">slate (dark)</option></select></Field>
                <Field label="Primary"><select class="input" value={str(p.primary) || 'indigo'} onChange={(e) => setPalette(i, { primary: (e.target as HTMLSelectElement).value })}>{props.site.colors.map((c) => <option key={c} value={c}>{c}</option>)}</select></Field>
                <Field label="Accent"><select class="input" value={str(p.accent) || 'indigo'} onChange={(e) => setPalette(i, { accent: (e.target as HTMLSelectElement).value })}>{props.site.colors.map((c) => <option key={c} value={c}>{c}</option>)}</select></Field>
                <Field label="Media query"><input class="input mono" value={str(p.media)} placeholder="(prefers-color-scheme: dark)" onInput={(e) => setPalette(i, { media: (e.target as HTMLInputElement).value })} /></Field>
                <Field label="Toggle icon"><input class="input mono" value={str(obj(p.toggle).icon)} placeholder="material/brightness-4" onInput={(e) => setPalette(i, { toggle: { ...obj(p.toggle), icon: (e.target as HTMLInputElement).value } })} /></Field>
                <Field label="Toggle label"><input class="input" value={str(obj(p.toggle).name)} placeholder="Switch to dark mode" onInput={(e) => setPalette(i, { toggle: { ...obj(p.toggle), name: (e.target as HTMLInputElement).value } })} /></Field>
              </div>
              <button class="btn btn--small btn--danger" onClick={() => setTheme('palette', palettes.filter((_, j) => j !== i))}>Remove palette</button>
            </fieldset>
          ))}
          <button class="btn btn--small" onClick={() => setTheme('palette', [...palettes, { scheme: palettes.length ? 'slate' : 'default', primary: 'indigo', accent: 'indigo' }])}>+ Add palette</button>
        </div>
        <div class="card">
          <h3>Features</h3>
          <p class="muted small">Optional behaviors of the Material theme. Each one links to its documentation.</p>
          <div class="feature-list">
            {[...new Set([...props.site.features, ...features])].map((f) => (
              <div class="feature" key={f}>
                <label class="check" title={featureHelp[f]?.text}><input type="checkbox" checked={features.includes(f)} onChange={(e) => setTheme('features', (e.target as HTMLInputElement).checked ? [...features, f] : features.filter((x) => x !== f))} /> <code>{f}</code></label>
                {featureHelp[f] && <p class="feature__help">{featureHelp[f].text} <a href={featureDocUrl(featureHelp[f].doc)} target="_blank" rel="noopener" title="Open the documentation for this feature">Docs ↗</a></p>}
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}

export function TemplatesScreen(props: { name: string | null }) {
  const list = useLoad(() => api.templates());
  const pages = useLoad(() => api.pages());
  const [name, setName] = useState<string | null>(props.name);
  const current = useLoad(() => (name ? api.template(name) : Promise.resolve(null)), [name]);
  const [text, setText] = useState('');
  const [showDiff, setShowDiff] = useState(false);
  const [live, setLiveState] = useState(() => { try { return localStorage.getItem('ndcms-template-live') !== 'off'; } catch { return true; } });
  const setLive = (v: boolean) => { setLiveState(v); try { localStorage.setItem('ndcms-template-live', v ? 'on' : 'off'); } catch { /* private mode */ } };
  /** Where a live edit is: waiting for typing to pause, saving, or waiting for the rebuilt page. */
  const [phase, setPhase] = useState<'idle' | 'pending' | 'saving' | 'rebuilding'>('idle');
  const [viewport, setViewportState] = useState<string>(() => { try { return localStorage.getItem('ndcms-template-viewport') ?? 'fit'; } catch { return 'fit'; } });
  const setViewport = (v: string) => { setViewportState(v); try { localStorage.setItem('ndcms-template-viewport', v); } catch { /* private mode */ } };
  const [liveError, setLiveError] = useState<string | null>(null);
  const [opened, setOpened] = useState<{ name: string; text: string; overridden: boolean } | null>(null);
  const [previewPath, setPreviewPath] = useState<string>(() => { try { return localStorage.getItem('ndcms-template-preview') ?? ''; } catch { return ''; } });
  const [creating, setCreating] = useState(false);
  const [frameKey, setFrameKey] = useState(0);
  const { busy, run } = useAction();
  useEffect(() => {
    if (!current.data) return;
    setText(current.data.text);
    // Remember the template as it was when opened, so live edits can be undone.
    if (!opened || opened.name !== current.data.name) setOpened({ name: current.data.name, text: current.data.text, overridden: current.data.overridden });
  }, [current.data]);

  // Live preview: write the draft (debounced) so the site rebuilds and the preview reloads.
  useEffect(() => {
    if (!live || !current.data || text === current.data.text) return;
    setPhase('pending');
    const handle = setTimeout(async () => {
      setPhase('saving');
      try {
        await api.saveTemplate(current.data!.name, text);
        setLiveError(null);
        setPhase('rebuilding');
        current.reload();
        list.reload();
      } catch (e) {
        setPhase('idle');
        setLiveError(e instanceof Error ? e.message : String(e));
      }
    }, 900);
    return () => clearTimeout(handle);
  }, [text, live]);
  // The preview reloads itself after the rebuild; give up waiting after a while.
  useEffect(() => {
    if (phase !== 'rebuilding') return;
    const t = setTimeout(() => setPhase('idle'), 15000);
    return () => clearTimeout(t);
  }, [phase]);

  if (!list.data) return <Loading error={list.error} />;
  const t = current.data;
  const dirty = !!t && text !== t.text;
  const diffText = t?.original != null && text !== t.original ? simpleDiff(t.original, text) : '';
  const livePages = (pages.data ?? []).filter((p) => p.url !== null);
  const previewPage = livePages.find((p) => p.path === previewPath) ?? livePages.find((p) => p.path === 'index.md') ?? livePages[0];
  const changedSinceOpen = !!opened && !!t && opened.name === t.name && (text !== opened.text || t.overridden !== opened.overridden);

  const undoAll = async () => {
    if (!opened || !t) return;
    if (!confirmAction(`Put ${t.name} back the way it was when you opened it?`)) return;
    if (opened.overridden) await run(() => api.saveTemplate(opened.name, opened.text), 'Restored');
    else await run(() => api.revertTemplate(opened.name), 'Restored the theme version');
    current.reload();
    list.reload();
  };

  const pageTemplatesList = list.data.filter((x) => !x.name.includes('/'));
  const partials = list.data.filter((x) => x.name.includes('/'));
  const item = (x: TemplateInfo) => (
    <li key={x.name}>
      <button class={`btn btn--link mono${x.name === name ? ' on' : ''}`} title={x.inTheme ? 'A theme template' : 'A template you added'} onClick={() => { if (!dirty || live || confirmAction('Drop unsaved template edits?')) { setName(x.name); setShowDiff(false); setOpened(null); location.hash = `#/templates?name=${encodeURIComponent(x.name)}`; } }}>{x.name}</button>
      {x.overridden && <span class="badge badge--warn" title={x.inTheme ? 'You changed this template' : 'Added by you'}>{x.inTheme ? 'custom' : 'new'}</span>}
    </li>
  );

  return (
    <section class="editor-screen">
      <header class="screen-head">
        <h1>Templates</h1>
        <div class="row">
          <button class="btn" title="Make a new page template that pages can choose in their settings" onClick={() => setCreating(true)}>+ New page template</button>
          {t && <>
            <span class="mono">{t.name}</span>
            {t.overridden ? <span class="badge badge--warn">customized</span> : <span class="badge">theme default</span>}
            {t.original !== null && <button class="btn" title="Show what you changed compared with the theme's version" onClick={() => setShowDiff(!showDiff)}>{showDiff ? 'Edit' : 'Compare with theme'}</button>}
            {t.overridden && <button class="btn btn--danger" disabled={busy} title={t.original === null ? 'Delete this template' : 'Delete your version and use the theme\'s again'} onClick={async () => { if (confirmAction(t.original === null ? `Delete the template ${t.name}? Pages that use it fall back to main.html.` : `Delete your version of ${t.name} and go back to the theme's?`)) { await run(() => api.revertTemplate(t.name), t.original === null ? 'Deleted' : 'Reverted'); setLive(false); current.reload(); list.reload(); } }}>{t.original === null ? 'Delete template' : 'Revert to theme'}</button>}
            {changedSinceOpen && <button class="btn" disabled={busy} title="Undo every change since you opened this template" onClick={() => void undoAll()}>↶ Undo all</button>}
            <label class="check" title="Save as you type so the preview shows your change within a second or two. Undo all puts it back."><input type="checkbox" checked={live} onChange={() => setLive(!live)} /> Live preview</label>
            <button class="btn btn--primary" disabled={busy || !dirty} onClick={async () => { const r = await run(() => api.saveTemplate(t.name, text).then(() => true), t.overridden ? 'Saved' : 'Saved as an override'); if (r) { current.reload(); list.reload(); } }}>Save</button>
          </>}
        </div>
      </header>
      <p class="muted small">Templates use Scriban. Saving writes a copy to the site's template overrides folder{list.data.some((x) => x.overridden) ? '' : ' (created as "overrides" and set as theme.customDir on first save)'}; the theme's own files are never changed. Page templates are the ones a page can pick in its settings; partials are pieces those pages include.</p>
      {liveError && <div class="card card--hint"><b>Not applied:</b> <span class="mono small">{liveError}</span></div>}
      <div class="templates-layout templates-layout--preview">
        <div class="template-list">
          <div class="field__label">Page templates</div>
          <ul class="plain">{pageTemplatesList.map(item)}</ul>
          <div class="field__label">Partials</div>
          <ul class="plain">{partials.map(item)}</ul>
        </div>
        <div class="editor-code">
          {!name && <p class="muted">Pick a template to view or customize it.</p>}
          {name && !t && <Loading error={current.error} />}
          {t && (showDiff ? <Diff text={diffText} /> : <CodeEditor language="html" value={text} onChange={setText} onSave={() => { if (dirty) void run(() => api.saveTemplate(t.name, text), 'Saved').then(() => { current.reload(); list.reload(); }); }} />)}
        </div>
        <div class="template-preview">
          <div class="row">
            <span class="field__label">Preview</span>
            <select class="input" value={previewPage?.path ?? ''} title="The page to preview" onChange={(e) => {
              const v = (e.target as HTMLSelectElement).value;
              setPreviewPath(v);
              try { localStorage.setItem('ndcms-template-preview', v); } catch { /* private mode */ }
            }}>
              {livePages.map((p) => <option key={p.path} value={p.path}>{p.title} ({p.path})</option>)}
            </select>
            <select class="input input--auto" value={viewport} title="Preview width" onChange={(e) => setViewport((e.target as HTMLSelectElement).value)}>
              {viewports.map((v) => <option key={v.id} value={v.id}>{v.label}</option>)}
            </select>
            <button class="btn btn--small" title="Reload the preview" onClick={() => setFrameKey((k) => k + 1)}>⟳ Reload</button>
          </div>
          {previewPage
            ? <ScaledFrame key={frameKey + previewPage.path} width={viewports.find((v) => v.id === viewport)?.width ?? null} src={'/' + previewPage.url + '?cms-preview=1'}
                onLoad={() => setPhase((p) => (p === 'rebuilding' ? 'idle' : p))}
                status={phase === 'idle' ? null : phase === 'pending' ? 'Waiting for you to pause typing…' : phase === 'saving' ? 'Saving the template…' : 'Regenerating the site…'} />
            : <p class="muted">No built pages to preview yet.</p>}
          <p class="muted small">{live ? 'Live: your edits are saved and shown here within a couple of seconds.' : 'Shows the saved templates. Turn on Live preview, or Save, to see your edits.'}</p>
        </div>
      </div>
      {creating && <NewTemplateDialog existing={list.data.map((x) => x.name)} onClose={() => setCreating(false)} onCreate={async (newName, from) => {
        const source = await api.template(from);
        const ok = await run(() => api.saveTemplate(newName, source.text).then(() => true), `Created ${newName}`);
        if (ok) { setCreating(false); list.reload(); setName(newName); setOpened(null); location.hash = `#/templates?name=${encodeURIComponent(newName)}`; }
      }} />}
    </section>
  );
}

function NewTemplateDialog(props: { existing: string[]; onClose: () => void; onCreate: (name: string, from: string) => void }) {
  const [name, setName] = useState('');
  const pagesOnly = props.existing.filter((n) => !n.includes('/') && n !== '404.html');
  const [from, setFrom] = useState(pagesOnly.includes('main.html') ? 'main.html' : pagesOnly[0] ?? '');
  const file = name.trim() ? (name.trim().toLowerCase().endsWith('.html') ? name.trim() : name.trim() + '.html').replace(/[^\w.-]+/g, '-') : '';
  const taken = props.existing.includes(file);
  return (
    <Modal title="New page template" onClose={props.onClose} actions={<>
      <button class="btn" onClick={props.onClose}>Cancel</button>
      <button class="btn btn--primary" disabled={!file || taken || !from} onClick={() => props.onCreate(file, from)}>Create</button>
    </>}>
      <Field label="Name" hint={taken ? 'A template with that name already exists.' : file ? `Saved as ${file} in the overrides folder. Pages choose it with template: ${file}` : 'For example landing or wide-page'}><input class="input mono" value={name} autoFocus onInput={(e) => setName((e.target as HTMLInputElement).value)} /></Field>
      <Field label="Start from" hint="A copy of this template is the starting point"><select class="input" value={from} onChange={(e) => setFrom((e.target as HTMLSelectElement).value)}>{pagesOnly.map((n) => <option key={n} value={n}>{n}</option>)}</select></Field>
    </Modal>
  );
}

/** A small line diff (LCS) between two texts, in unified-ish form. */
function simpleDiff(a: string, b: string): string {
  const x = a.split('\n');
  const y = b.split('\n');
  if (x.length * y.length > 4_000_000) return '(files too large to compare here)';
  const dp: number[][] = Array.from({ length: x.length + 1 }, () => new Array(y.length + 1).fill(0));
  for (let i = x.length - 1; i >= 0; i--) for (let j = y.length - 1; j >= 0; j--) dp[i][j] = x[i] === y[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
  const out: string[] = ['--- theme', '+++ yours'];
  let i = 0, j = 0;
  while (i < x.length || j < y.length) {
    if (i < x.length && j < y.length && x[i] === y[j]) { out.push(' ' + x[i]); i++; j++; }
    else if (j < y.length && (i >= x.length || dp[i][j + 1] >= dp[i + 1][j])) { out.push('+' + y[j]); j++; }
    else { out.push('-' + x[i]); i++; }
  }
  return out.join('\n');
}

export function SettingsScreen(props: { site: SiteInfo }) {
  const { data, error, reload } = useLoad(() => api.settings());
  const file = useLoad(() => api.configFile());
  const [settings, setSettings] = useState<Obj | null>(null);
  const [raw, setRaw] = useState<string | null>(null);
  const { busy, run } = useAction();
  useEffect(() => { if (data) setSettings(structuredClone(data)); }, [data]);
  useEffect(() => { if (file.data) setRaw(file.data.text); }, [file.data]);
  if (!data || !settings) return <Loading error={error} />;
  const dirty = JSON.stringify(settings) !== JSON.stringify(data);
  const labels: Record<string, [string, string?]> = {
    siteName: ['Site name'], siteUrl: ['Site URL', 'Public address, e.g. https://example.com/docs/'],
    siteDescription: ['Description'], siteAuthor: ['Author'], copyright: ['Copyright'],
    repoUrl: ['Repository URL'], repoName: ['Repository name'], editUri: ['Edit path', 'Appended to the repository URL for "edit this page" links, e.g. edit/main/docs/'],
  };
  return (
    <section>
      <header class="screen-head">
        <h1>Settings</h1>
        <div class="row">
          <button class="btn" disabled={!dirty} onClick={() => setSettings(structuredClone(data))}>Revert</button>
          <button class="btn btn--primary" disabled={!dirty || busy} onClick={async () => { const r = await run(() => api.saveSettings(settings), 'Settings saved'); if (r) { reload(); file.reload(); } }}>Save</button>
        </div>
      </header>
      <div class="card">
        <div class="grid2">
          {props.site.siteKeys.map((k) => (
            <Field key={k} label={labels[k]?.[0] ?? k} hint={labels[k]?.[1]}>
              <input class="input" value={str(settings[k])} onInput={(e) => setSettings({ ...settings, [k]: (e.target as HTMLInputElement).value })} />
            </Field>
          ))}
        </div>
      </div>
      <div class="card">
        <h3>{props.site.configFile} <span class="muted small">everything else: plugins, markdown extensions, deploy, optimization…</span></h3>
        {raw === null ? <Loading error={file.error} /> : <>
          <div class="editor-code editor-code--short"><CodeEditor language="json" value={raw} onChange={setRaw} /></div>
          <div class="row">
            <button class="btn" disabled={raw === file.data?.text} onClick={() => setRaw(file.data!.text)}>Revert</button>
            <button class="btn btn--primary" disabled={busy || raw === file.data?.text} onClick={async () => { const r = await run(() => api.saveConfigFile(raw).then(() => true), 'Saved. The site is rebuilding.'); if (r) { file.reload(); reload(); } }}>Save file</button>
          </div>
          <p class="muted small">The file is checked before it is saved; comments are allowed.</p>
        </>}
      </div>
    </section>
  );
}

export { toast };
