// Netdocs CMS admin app.
import { render } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import './admin.css';
import { api, formatBytes, type SiteInfo } from '../shared/api';
import { Loading, Toasts, useLoad } from './ui';
import { useRoute } from './router';
import { PageEditorScreen, PagesScreen } from './pages';
import { NavScreen } from './nav';
import { TaxonomyScreen } from './taxonomy';
import { MediaScreen } from './media';
import { GitScreen } from './git';
import { SettingsScreen, TemplatesScreen, ThemeScreen } from './theme';

const sections = [
  { path: '/', label: 'Dashboard', icon: '◧' },
  { path: '/pages', label: 'Pages', icon: '📄' },
  { path: '/nav', label: 'Navigation', icon: '☰' },
  { path: '/taxonomy', label: 'Tags & categories', icon: '🏷' },
  { path: '/media', label: 'Media', icon: '🖼' },
  { path: '/git', label: 'Source control', icon: '⎇' },
  { path: '/theme', label: 'Theme', icon: '🎨' },
  { path: '/templates', label: 'Templates', icon: '⟨⟩' },
  { path: '/settings', label: 'Settings', icon: '⚙' },
];

function App() {
  const route = useRoute();
  const site = useLoad(() => api.site());
  const [menu, setMenu] = useState(false);
  useEffect(() => setMenu(false), [route.path]);
  useEffect(() => { if (site.data) document.title = `${site.data.siteName} · CMS`; }, [site.data]);

  // Reload screens when the site rebuilds (e.g. after a save or an edit in another tool).
  const [rebuilds, setRebuilds] = useState(0);
  useEffect(() => {
    let socket: WebSocket | null = null;
    let stopped = false;
    const connect = () => {
      socket = new WebSocket(`ws://${location.host}/__livereload`);
      socket.onmessage = (e) => { if (e.data === 'reload') setRebuilds((n) => n + 1); };
      socket.onclose = () => { if (!stopped) setTimeout(connect, 2000); };
    };
    connect();
    return () => { stopped = true; socket?.close(); };
  }, []);

  if (!site.data) return <div class="boot"><Loading error={site.error} /></div>;

  const active = (p: string) => (p === '/' ? route.path === '/' : route.path === p || route.path.startsWith(p + '/'));
  let screen;
  switch (route.path) {
    case '/': screen = <Dashboard key={rebuilds} site={site.data} />; break;
    case '/pages': screen = <PagesScreen key={rebuilds} />; break;
    case '/pages/edit': screen = <PageEditorScreen path={route.params.get('path') ?? ''} />; break;
    case '/nav': screen = <NavScreen />; break;
    case '/taxonomy': screen = <TaxonomyScreen key={(route.params.get('field') ?? 'tags') + rebuilds} field={route.params.get('field') ?? 'tags'} />; break;
    case '/media': screen = <MediaScreen key={rebuilds} />; break;
    case '/git': screen = <GitScreen key={rebuilds} />; break;
    case '/theme': screen = <ThemeScreen site={site.data} />; break;
    case '/templates': screen = <TemplatesScreen name={route.params.get('name')} />; break;
    case '/settings': screen = <SettingsScreen site={site.data} />; break;
    default: screen = <p>Not found. <a href="#/">Back to the dashboard</a></p>;
  }

  return (
    <div class={`shell${menu ? ' shell--menu' : ''}`}>
      <aside class="sidebar">
        <div class="brand"><strong>{site.data.siteName}</strong><span class="muted small">Netdocs CMS</span></div>
        <nav>
          {sections.map((s) => <a key={s.path} href={'#' + s.path} class={active(s.path) ? 'on' : ''}><span class="icon">{s.icon}</span>{s.label}</a>)}
        </nav>
        <a class="btn view-site" href="/" target="_blank" rel="noopener">View site ↗</a>
      </aside>
      <button class="menu-toggle btn" onClick={() => setMenu(!menu)} aria-label="Menu">☰</button>
      <main class="content">{screen}</main>
      <Toasts />
    </div>
  );
}

function Dashboard(props: { site: SiteInfo }) {
  const pages = useLoad(() => api.pages());
  const media = useLoad(() => api.media());
  const tags = useLoad(() => api.taxonomy('tags'));
  const git = useLoad(() => api.gitStatus().catch(() => null));
  if (!pages.data) return <Loading error={pages.error} />;
  const recent = [...pages.data].sort((a, b) => b.modified.localeCompare(a.modified)).slice(0, 8);
  const drafts = pages.data.filter((p) => p.draft).length;
  const unlisted = pages.data.filter((p) => !p.inNav).length;
  const convertible = media.data?.filter((m) => /\.(png|jpe?g|gif|bmp)$/i.test(m.path)) ?? [];
  const unused = media.data?.filter((m) => m.references === 0).length ?? 0;
  return (
    <section>
      <header class="screen-head"><h1>{props.site.siteName}</h1><a class="btn btn--primary" href="#/pages">Manage pages</a></header>
      <div class="stats">
        <a class="stat" href="#/pages"><strong>{pages.data.length}</strong><span>pages</span></a>
        <a class="stat" href="#/pages"><strong>{drafts}</strong><span>drafts</span></a>
        <a class="stat" href="#/nav"><strong>{unlisted}</strong><span>not in navigation</span></a>
        <a class="stat" href="#/taxonomy"><strong>{tags.data?.terms.length ?? '…'}</strong><span>tags</span></a>
        <a class="stat" href="#/media"><strong>{media.data?.length ?? '…'}</strong><span>media files{media.data ? ` · ${formatBytes(media.data.reduce((n, m) => n + m.size, 0))}` : ''}</span></a>
        <a class="stat" href="#/git"><strong>{git.data?.isRepository ? git.data.files.length : '–'}</strong><span>{git.data?.isRepository ? `uncommitted on ${git.data.branch}` : 'no git repository'}</span></a>
      </div>
      <div class="grid2">
        <div class="card">
          <h3>Recently changed</h3>
          <ul class="plain">{recent.map((p) => <li key={p.path}><a href={`#/pages/edit?path=${encodeURIComponent(p.path)}`}>{p.title}</a> <span class="muted small">{new Date(p.modified).toLocaleString()}</span></li>)}</ul>
        </div>
        <div class="card">
          <h3>Worth a look</h3>
          <ul class="plain">
            {(tags.data?.similar.length ?? 0) > 0 && <li>🏷 {tags.data!.similar.length} group(s) of tags look like duplicates. <a href="#/taxonomy">Review</a></li>}
            {convertible.length > 0 && <li>🖼 {convertible.length} PNG/JPEG image(s) could be converted to WebP. <a href="#/media">Optimize</a></li>}
            {unused > 0 && <li>🗑 {unused} media file(s) are not used by any page. <a href="#/media">Review</a></li>}
            {unlisted > 0 && <li>☰ {unlisted} page(s) are not in the navigation. <a href="#/nav">Review</a></li>}
            {(git.data?.files.length ?? 0) > 0 && <li>⎇ {git.data!.files.length} uncommitted change(s). <a href="#/git">Commit</a></li>}
          </ul>
          <p class="muted small">Tip: every page of the running site has an <b>✎ Edit page</b> button for editing in place.</p>
        </div>
      </div>
    </section>
  );
}

render(<App />, document.getElementById('app')!);
