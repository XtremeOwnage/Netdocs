// Source control: status, diff, stage/unstage/discard, commit, branches, pull and push.
import { useEffect, useState } from 'preact/hooks';
import { api, type GitFile, type GitResult } from '../shared/api';
import { Diff, Field, Loading, Modal, confirmAction, toast, useAction, useLoad } from './ui';

const describe = (f: GitFile) => {
  const code = f.index !== ' ' && f.index !== '?' ? f.index : f.workTree;
  return ({ M: 'modified', A: 'added', D: 'deleted', R: 'renamed', C: 'copied', '?': 'new', U: 'conflict' } as Record<string, string>)[code] ?? code;
};

export function GitScreen() {
  const status = useLoad(() => api.gitStatus());
  const log = useLoad(() => api.gitLog(20));
  const branches = useLoad(() => api.gitBranches());
  const [diff, setDiff] = useState<{ path: string | null; staged: boolean; text: string } | null>(null);
  const [message, setMessage] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [newBranch, setNewBranch] = useState(false);
  const [output, setOutput] = useState<string | null>(null);
  const { busy, run } = useAction();

  const refresh = () => { status.reload(); log.reload(); branches.reload(); };
  useEffect(() => { const t = setInterval(() => status.reload(), 10000); return () => clearInterval(t); }, []);

  if (!status.data) return <Loading error={status.error} />;
  const s = status.data;
  if (!s.isRepository) return <section><h1>Source control</h1><p>This site is not inside a git repository. Run <code>git init</code> in the site folder to start tracking changes.</p></section>;

  const staged = s.files.filter((f) => f.index !== ' ' && f.index !== '?');
  const unstaged = s.files.filter((f) => f.workTree !== ' ' || f.index === '?');

  const showDiff = async (path: string | null, isStaged: boolean) => {
    // Images have no text diff: show the committed and current pictures side by side.
    if (path && isImage(path)) { setDiff({ path, staged: isStaged, text: '' }); return; }
    const r = await run(() => api.gitDiff(path, isStaged));
    if (r) setDiff({ path, staged: isStaged, text: r.diff });
  };
  const act = async (fn: () => Promise<GitResult>, success: string) => {
    const r = await run(fn, success);
    if (r?.output?.trim()) setOutput(r.output.trim());
    setSelected(new Set());
    refresh();
    return r;
  };
  const pick = (files: GitFile[]) => files.filter((f) => selected.has(f.path)).map((f) => f.path);

  const fileRow = (f: GitFile, isStaged: boolean, name: string) => (
    <li key={(isStaged ? 's:' : 'u:') + f.path}>
      <input type="checkbox" checked={selected.has(f.path)} onChange={() => { const n = new Set(selected); if (n.has(f.path)) n.delete(f.path); else n.add(f.path); setSelected(n); }} aria-label={`Select ${f.path}`} />
      <span class={`git-state git-state--${describe(f)}`}>{describe(f)}</span>
      <button class="btn btn--link mono" title={`Show the changes in ${f.path}`} onClick={() => void showDiff(f.path, isStaged)}>{name}</button>
      {f.originalPath && <span class="muted small"> from {f.originalPath}</span>}
      <span class="git-files__actions">
        {isStaged
          ? <button class="btn btn--small" title="Take this file out of the next commit" onClick={() => void act(() => api.gitUnstage([f.path]), 'Unstaged')}>Unstage</button>
          : <>
            <button class="btn btn--small" title="Include this file in the next commit" onClick={() => void act(() => api.gitStage([f.path]), 'Staged')}>Stage</button>
            <button class="btn btn--small btn--danger" title="Throw away the changes to this file" onClick={() => { if (confirmAction(`Discard changes to ${f.path}? This cannot be undone.`)) void act(() => api.gitDiscard([f.path]), 'Discarded'); }}>Discard</button>
          </>}
      </span>
    </li>
  );

  // Changes grouped by folder: a blog post's markdown and its images usually share one.
  const fileList = (files: GitFile[], isStaged: boolean) => {
    const groups = new Map<string, GitFile[]>();
    for (const f of [...files].sort((a, b) => a.path.localeCompare(b.path))) {
      const dir = f.path.includes('/') ? f.path.slice(0, f.path.lastIndexOf('/')) : '';
      groups.set(dir, [...(groups.get(dir) ?? []), f]);
    }
    if (groups.size === 1 && files.length < 4) return <ul class="git-files">{files.map((f) => fileRow(f, isStaged, f.path))}</ul>;
    return (
      <div class="git-tree">
        {[...groups].map(([dir, list]) => {
          const paths = list.map((f) => f.path);
          const all = paths.every((p) => selected.has(p));
          return (
            <details key={dir} open class="git-folder">
              <summary>
                <input type="checkbox" checked={all} aria-label={`Select everything in ${dir || 'the top folder'}`} onClick={(e) => e.stopPropagation()} onChange={() => {
                  const n = new Set(selected);
                  for (const p of paths) { if (all) n.delete(p); else n.add(p); }
                  setSelected(n);
                }} />
                <span class="mono">📁 {dir || '(top folder)'}</span> <span class="muted small">{list.length} file{list.length === 1 ? '' : 's'}</span>
                <span class="git-files__actions">
                  {isStaged
                    ? <button class="btn btn--small" title="Take this folder's files out of the next commit" onClick={(e) => { e.preventDefault(); void act(() => api.gitUnstage(paths), 'Unstaged'); }}>Unstage folder</button>
                    : <>
                      <button class="btn btn--small" title="Include this folder's files in the next commit" onClick={(e) => { e.preventDefault(); void act(() => api.gitStage(paths), 'Staged'); }}>Stage folder</button>
                      <button class="btn btn--small btn--danger" title="Throw away the changes in this folder" onClick={(e) => { e.preventDefault(); if (confirmAction(`Discard changes to ${paths.length} file(s) in ${dir || 'the top folder'}? This cannot be undone.`)) void act(() => api.gitDiscard(paths), 'Discarded'); }}>Discard folder</button>
                    </>}
                </span>
              </summary>
              <ul class="git-files">{list.map((f) => fileRow(f, isStaged, f.path.slice(dir ? dir.length + 1 : 0)))}</ul>
            </details>
          );
        })}
      </div>
    );
  };

  return (
    <section>
      <header class="screen-head">
        <h1>Source control</h1>
        <div class="row">
          <span class="branch">⎇ <select class="input input--auto" value={branches.data?.current ?? s.branch ?? ''} onChange={(e) => {
            const b = (e.target as HTMLSelectElement).value;
            if (b === '__new') { setNewBranch(true); return; }
            if (s.files.length && !confirmAction(`Switch to ${b}? Uncommitted changes travel with you, or git refuses if they conflict.`)) return;
            void act(() => api.gitCheckout(b.replace(/^[^/]+\//, ''), false), `Switched to ${b}`);
          }}>
            {(branches.data?.local ?? [s.branch ?? '']).map((b) => <option key={b} value={b}>{b}</option>)}
            {branches.data?.remote.filter((r) => !branches.data!.local.includes(r.replace(/^[^/]+\//, ''))).map((r) => <option key={r} value={r}>{r} (remote)</option>)}
            <option value="__new">+ New branch…</option>
          </select></span>
          {s.upstream && <span class="muted small">{s.ahead} ahead · {s.behind} behind {s.upstream}</span>}
          <button class="btn" disabled={busy} title="Download new commits from the remote without changing your files" onClick={() => void act(() => api.gitFetch(), 'Fetched')}>Fetch</button>
          <button class="btn" disabled={busy} title="Download and apply new commits from the remote (fast-forward only)" onClick={() => void act(() => api.gitPull(), 'Pulled')}>Pull</button>
          <button class="btn" disabled={busy} title={`Upload your commits to ${s.pushTo ?? 'the remote'}`} onClick={() => {
            const target = s.pushTo ?? `origin/${s.branch}`;
            const fresh = s.upstream !== target;
            if (confirmAction(fresh ? `Push ${s.branch} to ${target}? This creates or updates that branch on the remote and makes it this branch's upstream.` : `Push ${s.branch} to ${target}?`)) void act(() => api.gitPush(), `Pushed to ${target}`);
          }}>Push{s.ahead ? ` (${s.ahead})` : ''}</button>
          <button class="btn btn--ghost" title="Check git again for changes" onClick={refresh}>⟳ Refresh</button>
        </div>
      </header>

      <div class="git-layout">
        <div>
          <div class="card">
            <Field label="Commit message">
              <textarea class="input" rows={3} value={message} placeholder="Describe the change" onInput={(e) => setMessage((e.target as HTMLTextAreaElement).value)} />
            </Field>
            <div class="row">
              <button class="btn btn--primary" disabled={busy || !message.trim() || staged.length === 0} onClick={async () => { const r = await act(() => api.gitCommit(message, false), 'Committed'); if (r) setMessage(''); }}>Commit {staged.length} staged</button>
              <button class="btn" disabled={busy || !message.trim() || s.files.length === 0} onClick={async () => { const r = await act(() => api.gitCommit(message, true), 'Committed'); if (r) setMessage(''); }}>Stage all and commit</button>
            </div>
          </div>

          <div class="card">
            <h3>Staged ({staged.length}) {staged.length > 0 && <button class="btn btn--small" onClick={() => void act(() => api.gitUnstage(pick(staged).length ? pick(staged) : staged.map((f) => f.path)), 'Unstaged')}>{pick(staged).length ? 'Unstage selected' : 'Unstage all'}</button>} {staged.length > 0 && <button class="btn btn--small" onClick={() => void showDiff(null, true)}>View diff</button>}</h3>
            {staged.length ? fileList(staged, true) : <p class="muted">Nothing staged.</p>}
          </div>

          <div class="card">
            <h3>Changes ({unstaged.length}) {unstaged.length > 0 && <>
              <button class="btn btn--small" onClick={() => void act(() => api.gitStage(pick(unstaged)), 'Staged')}>{pick(unstaged).length ? 'Stage selected' : 'Stage all'}</button>
              <button class="btn btn--small btn--danger" onClick={() => {
                const paths = pick(unstaged);
                if (confirmAction(paths.length ? `Discard changes to ${paths.length} file(s)? This cannot be undone.` : 'Discard ALL uncommitted changes in the site folder? New files are deleted. This cannot be undone.'))
                  void act(() => api.gitDiscard(paths), 'Discarded');
              }}>{pick(unstaged).length ? 'Discard selected' : 'Discard all'}</button>
            </>}</h3>
            {unstaged.length ? fileList(unstaged, false) : <p class="muted">Working tree clean.</p>}
          </div>

          {output && <div class="card"><h3>git output <button class="btn btn--small" onClick={() => setOutput(null)}>✕</button></h3><pre class="output">{output}</pre></div>}
        </div>

        <div class="card">
          <h3>History</h3>
          {log.data ? (
            <ul class="plain git-log">
              {log.data.map((c) => <li key={c.hash}><code>{c.hash}</code> {c.subject} <span class="muted small">{c.author}, {new Date(c.date).toLocaleString()}</span></li>)}
            </ul>
          ) : <Loading error={log.error} />}
        </div>
      </div>

      {diff && <Modal wide title={`${diff.path ?? 'All files'}${diff.staged ? ' (staged)' : ''}`} onClose={() => setDiff(null)}>
        {diff.path && isImage(diff.path) ? <ImageChange path={diff.path} /> : diff.text.trim() ? <Diff text={diff.text} /> : <p class="muted">No text changes to show (the file may be binary, or only its permissions changed).</p>}
      </Modal>}
      {newBranch && <NewBranchDialog onClose={() => setNewBranch(false)} onCreate={async (name) => { setNewBranch(false); await act(() => api.gitCheckout(name, true), `Created ${name}`); }} />}
    </section>
  );
}

function NewBranchDialog(props: { onClose: () => void; onCreate: (name: string) => void }) {
  const [name, setName] = useState('');
  return (
    <Modal title="New branch" onClose={props.onClose} actions={<>
      <button class="btn" onClick={props.onClose}>Cancel</button>
      <button class="btn btn--primary" disabled={!name.trim()} onClick={() => props.onCreate(name.trim())}>Create and switch</button>
    </>}>
      <Field label="Branch name" hint="Created from the current branch; uncommitted changes come along."><input class="input mono" value={name} autoFocus onInput={(e) => setName((e.target as HTMLInputElement).value)} /></Field>
    </Modal>
  );
}

export function toastGit(r: GitResult | undefined) { if (r?.output) toast(r.output.split('\n')[0], 'info'); }

const isImage = (path: string) => /\.(png|jpe?g|gif|webp|avif|svg|bmp|ico)$/i.test(path);

/** The last committed version of an image next to the current one. */
function ImageChange(props: { path: string }) {
  const [missing, setMissing] = useState<{ before: boolean; after: boolean }>({ before: false, after: false });
  return (
    <div class="image-change">
      <figure>
        <figcaption>Last commit</figcaption>
        {missing.before ? <p class="muted">Not in the last commit (new file).</p>
          : <img src={api.gitFileUrl(props.path, true)} alt="Before" onError={() => setMissing((m) => ({ ...m, before: true }))} />}
      </figure>
      <figure>
        <figcaption>Now</figcaption>
        {missing.after ? <p class="muted">Deleted.</p>
          : <img src={api.gitFileUrl(props.path, false) + `&v=${Date.now()}`} alt="After" onError={() => setMissing((m) => ({ ...m, after: true }))} />}
      </figure>
    </div>
  );
}
