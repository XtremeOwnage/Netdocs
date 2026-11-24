// Navigation editor for the `nav` array in appsettings.json.
import { useEffect, useState } from 'preact/hooks';
import { api, type Json } from '../shared/api';
import { Loading, confirmAction, useAction, useLoad } from './ui';

interface Node { id: number; title: string; path: string; icon: string; extra: Record<string, Json>; children: Node[] | null }

let nextId = 1;

function fromJson(items: Json[]): Node[] {
  return items.map((raw) => {
    const o = (raw ?? {}) as Record<string, Json>;
    const { title, path, icon, children, ...extra } = o;
    return {
      id: nextId++,
      title: typeof title === 'string' ? title : '',
      path: typeof path === 'string' ? path : '',
      icon: typeof icon === 'string' ? icon : '',
      extra,
      children: Array.isArray(children) ? fromJson(children) : null,
    };
  });
}

function toJson(nodes: Node[]): Json[] {
  return nodes.map((n) => {
    const o: Record<string, Json> = {};
    if (n.title) o.title = n.title;
    if (n.children === null) o.path = n.path;
    if (n.icon) o.icon = n.icon;
    Object.assign(o, n.extra);
    if (n.children !== null) o.children = toJson(n.children);
    return o;
  });
}

type Path = number[];

function at(nodes: Node[], path: Path): { list: Node[]; index: number } {
  let list = nodes;
  for (let i = 0; i < path.length - 1; i++) list = list[path[i]].children!;
  return { list, index: path[path.length - 1] };
}

export function NavScreen() {
  const { data, error, reload } = useLoad(() => api.nav());
  const [tree, setTree] = useState<Node[] | null>(null);
  const [dirty, setDirty] = useState(false);
  const [drag, setDrag] = useState<Path | null>(null);
  const { busy, run } = useAction();

  useEffect(() => { if (data) { setTree(fromJson(data.nav)); setDirty(false); } }, [data]);
  if (!data || !tree) return <Loading error={error} />;

  const titles = Object.fromEntries(data.pages.map((p) => [p.path, p.title]));
  const listed = new Set<string>();
  const walk = (ns: Node[]) => ns.forEach((n) => { if (n.children) walk(n.children); else listed.add(n.path); });
  walk(tree);
  const unlisted = data.pages.filter((p) => !listed.has(p.path));

  const mutate = (fn: (t: Node[]) => void) => {
    const copy = structuredClone(tree) as Node[];
    fn(copy);
    setTree(copy);
    setDirty(true);
  };

  // Moves by node identity, so removing the source never shifts the drop target.
  const moveTo = (from: Path, to: Path, inside: boolean) => mutate((t) => {
    const targetId = (() => { const d = at(t, to); return d.list[d.index].id; })();
    const src = at(t, from);
    const [node] = src.list.splice(src.index, 1);
    const find = (list: Node[]): boolean => {
      const i = list.findIndex((n) => n.id === targetId);
      if (i >= 0) {
        if (inside && list[i].children) list[i].children!.push(node);
        else list.splice(i, 0, node);
        return true;
      }
      return list.some((n) => n.children !== null && find(n.children));
    };
    if (!find(t)) t.push(node);
  });

  const isAncestor = (a: Path, b: Path) => a.length <= b.length && a.every((v, i) => v === b[i]);

  const render = (nodes: Node[], prefix: Path): preact.JSX.Element => (
    <ul class="nav-tree">
      {nodes.map((n, i) => {
        const path = [...prefix, i];
        const update = (patch: Partial<Node>) => mutate((t) => { const p = at(t, path); Object.assign(p.list[p.index], patch); });
        return (
          <li key={n.id}
            onDragOver={(e) => { if (drag && !isAncestor(drag, path)) { e.preventDefault(); e.stopPropagation(); } }}
            onDrop={(e) => {
              e.preventDefault();
              e.stopPropagation();
              if (!drag || isAncestor(drag, path)) return;
              const rect = (e.currentTarget as HTMLElement).getBoundingClientRect();
              const inside = !!n.children && e.clientY > rect.top + 16;
              moveTo(drag, path, inside);
              setDrag(null);
            }}>
            <div class={`nav-node${n.children ? ' nav-node--section' : ''}`} draggable onDragStart={(e) => { e.stopPropagation(); setDrag(path); }} onDragEnd={() => setDrag(null)}>
              <span class="drag" title="Drag to move">⠿</span>
              <input class="input input--title" value={n.title} placeholder={n.children ? 'Section title' : titles[n.path] ?? 'Page title (optional)'} onInput={(e) => update({ title: (e.target as HTMLInputElement).value })} />
              {n.children === null && (
                <select class="input input--path" value={n.path} onChange={(e) => update({ path: (e.target as HTMLSelectElement).value })}>
                  {!titles[n.path] && <option value={n.path}>{n.path} (missing)</option>}
                  {data.pages.map((p) => <option key={p.path} value={p.path}>{p.path}</option>)}
                </select>
              )}
              <input class="input input--icon" value={n.icon} placeholder="icon" title="Icon name, e.g. home or material/book" onInput={(e) => update({ icon: (e.target as HTMLInputElement).value })} />
              <span class="nav-node__actions">
                <button class="btn btn--small" title="Move up" disabled={i === 0} onClick={() => mutate((t) => { const p = at(t, path); [p.list[i - 1], p.list[i]] = [p.list[i], p.list[i - 1]]; })}>↑</button>
                <button class="btn btn--small" title="Move down" disabled={i === nodes.length - 1} onClick={() => mutate((t) => { const p = at(t, path); [p.list[i + 1], p.list[i]] = [p.list[i], p.list[i + 1]]; })}>↓</button>
                <button class="btn btn--small" title="Indent into the section above" disabled={i === 0 || !nodes[i - 1].children} onClick={() => mutate((t) => { const p = at(t, path); const [node] = p.list.splice(i, 1); p.list[i - 1].children!.push(node); })}>→</button>
                <button class="btn btn--small" title="Move out of this section" disabled={prefix.length === 0} onClick={() => mutate((t) => {
                  const p = at(t, path);
                  const [node] = p.list.splice(i, 1);
                  const parent = at(t, prefix);
                  parent.list.splice(parent.index + 1, 0, node);
                })}>←</button>
                {n.children && <button class="btn btn--small" title="Add a page to this section" onClick={() => mutate((t) => { const p = at(t, path); p.list[p.index].children!.push({ id: nextId++, title: '', path: unlisted[0]?.path ?? data.pages[0]?.path ?? '', icon: '', extra: {}, children: null }); })}>+ page</button>}
                <button class="btn btn--small btn--danger" title="Remove from navigation (the page itself is kept)" onClick={() => {
                  if (n.children?.length && !confirmAction(`Remove section "${n.title}" and its ${n.children.length} entries from the navigation?`)) return;
                  mutate((t) => { const p = at(t, path); p.list.splice(i, 1); });
                }}>✕</button>
              </span>
            </div>
            {n.children && render(n.children, path)}
          </li>
        );
      })}
    </ul>
  );

  return (
    <section>
      <header class="screen-head">
        <h1>Navigation</h1>
        <div class="row">
          <button class="btn" onClick={() => mutate((t) => t.push({ id: nextId++, title: 'New section', path: '', icon: '', extra: {}, children: [] }))}>+ Section</button>
          <button class="btn" disabled={!dirty} onClick={() => reload()}>Revert</button>
          <button class="btn btn--primary" disabled={!dirty || busy} onClick={async () => { const r = await run(() => api.saveNav(toJson(tree)), 'Navigation saved'); if (r) setDirty(false); }}>Save</button>
        </div>
      </header>
      {tree.length === 0 && <p class="muted">The site has no explicit navigation, so pages are listed automatically from the docs folder. Add entries to take control of the order.</p>}
      <div class="nav-layout">
        <div class="card">{render(tree, [])}</div>
        <aside class="card">
          <h3>Not in navigation ({unlisted.length})</h3>
          <ul class="plain">
            {unlisted.map((p) => (
              <li key={p.path}>
                <button class="btn btn--small" onClick={() => mutate((t) => t.push({ id: nextId++, title: '', path: p.path, icon: '', extra: {}, children: null }))}>+ Add</button>
                {' '}<span title={p.path}>{p.title}</span> <span class="muted mono small">{p.path}</span>
              </li>
            ))}
          </ul>
        </aside>
      </div>
    </section>
  );
}
