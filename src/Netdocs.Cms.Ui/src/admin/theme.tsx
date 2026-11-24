// Theme settings, template overrides, site settings and the raw appsettings.json editor.
import { useEffect, useState } from 'preact/hooks';
import { api, type Json, type SiteInfo } from '../shared/api';
import { CodeEditor, Diff, Field, Loading, confirmAction, toast, useAction, useLoad } from './ui';

type Obj = Record<string, Json>;
const obj = (v: Json | undefined): Obj => (v && typeof v === 'object' && !Array.isArray(v) ? v as Obj : {});
const str = (v: Json | undefined) => (typeof v === 'string' ? v : '');

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
          <div class="feature-list">
            {[...new Set([...props.site.features, ...features])].map((f) => (
              <label class="check" key={f}><input type="checkbox" checked={features.includes(f)} onChange={(e) => setTheme('features', (e.target as HTMLInputElement).checked ? [...features, f] : features.filter((x) => x !== f))} /> <code>{f}</code></label>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}

export function TemplatesScreen(props: { name: string | null }) {
  const list = useLoad(() => api.templates());
  const [name, setName] = useState<string | null>(props.name);
  const current = useLoad(() => (name ? api.template(name) : Promise.resolve(null)), [name]);
  const [text, setText] = useState('');
  const [showDiff, setShowDiff] = useState(false);
  const { busy, run } = useAction();
  useEffect(() => { if (current.data) setText(current.data.text); }, [current.data]);
  if (!list.data) return <Loading error={list.error} />;
  const t = current.data;
  const dirty = !!t && text !== t.text;
  const diffText = t?.original != null && text !== t.original ? simpleDiff(t.original, text) : '';

  return (
    <section class="editor-screen">
      <header class="screen-head">
        <h1>Templates</h1>
        {t && <div class="row">
          <span class="mono">{t.name}</span>
          {t.overridden ? <span class="badge badge--warn">customized</span> : <span class="badge">theme default</span>}
          {t.original !== null && <button class="btn" onClick={() => setShowDiff(!showDiff)}>{showDiff ? 'Edit' : 'Compare with theme'}</button>}
          {t.overridden && <button class="btn btn--danger" disabled={busy} onClick={async () => { if (confirmAction(`Delete your version of ${t.name} and go back to the theme's?`)) { await run(() => api.revertTemplate(t.name), 'Reverted'); current.reload(); list.reload(); } }}>Revert to theme</button>}
          <button class="btn btn--primary" disabled={busy || !dirty} onClick={async () => { const r = await run(() => api.saveTemplate(t.name, text).then(() => true), t.overridden ? 'Saved' : 'Saved as an override'); if (r) { current.reload(); list.reload(); } }}>Save</button>
        </div>}
      </header>
      <p class="muted small">Templates use Scriban. Saving writes a copy to the site's template overrides folder{list.data.some((x) => x.overridden) ? '' : ' (created as "overrides" and set as theme.customDir on first save)'}; the theme's own files are never changed.</p>
      <div class="templates-layout">
        <ul class="plain template-list">
          {list.data.map((x) => (
            <li key={x.name}><button class={`btn btn--link mono${x.name === name ? ' on' : ''}`} onClick={() => { if (!dirty || confirmAction('Drop unsaved template edits?')) { setName(x.name); setShowDiff(false); location.hash = `#/templates?name=${encodeURIComponent(x.name)}`; } }}>{x.name}</button>{x.overridden && <span class="badge badge--warn">custom</span>}</li>
          ))}
        </ul>
        <div class="editor-code">
          {!name && <p class="muted">Pick a template to view or customize it.</p>}
          {name && !t && <Loading error={current.error} />}
          {t && (showDiff ? <Diff text={diffText} /> : <CodeEditor language="html" value={text} onChange={setText} onSave={() => { if (dirty) void run(() => api.saveTemplate(t.name, text), 'Saved').then(() => { current.reload(); list.reload(); }); }} />)}
        </div>
      </div>
    </section>
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
