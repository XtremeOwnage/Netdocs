// Shared admin components: code editor, dialogs, toasts, small form helpers.
import { useEffect, useRef, useState } from 'preact/hooks';
import type { ComponentChildren } from 'preact';
import { EditorView, basicSetup } from 'codemirror';
import { EditorState, type Extension } from '@codemirror/state';
import { keymap } from '@codemirror/view';
import { indentWithTab } from '@codemirror/commands';
import { markdown } from '@codemirror/lang-markdown';
import { json } from '@codemirror/lang-json';
import { html } from '@codemirror/lang-html';
import { oneDark } from '@codemirror/theme-one-dark';

const prefersDark = () => window.matchMedia?.('(prefers-color-scheme: dark)').matches;

export function CodeEditor(props: {
  value: string;
  onChange: (value: string) => void;
  language?: 'markdown' | 'json' | 'html';
  onSave?: () => void;
  height?: string;
}) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const latest = useRef(props);
  latest.current = props;

  useEffect(() => {
    const lang: Extension = props.language === 'json' ? json() : props.language === 'html' ? html() : markdown();
    const extensions: Extension[] = [
      basicSetup,
      lang,
      EditorView.lineWrapping,
      keymap.of([indentWithTab, { key: 'Mod-s', preventDefault: true, run: () => { latest.current.onSave?.(); return true; } }]),
      EditorView.updateListener.of((u) => { if (u.docChanged) latest.current.onChange(u.state.doc.toString()); }),
      EditorView.theme({ '&': { height: props.height ?? '100%' }, '.cm-scroller': { fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace' } }),
    ];
    if (prefersDark()) extensions.push(oneDark);
    view.current = new EditorView({ state: EditorState.create({ doc: props.value, extensions }), parent: host.current! });
    return () => view.current?.destroy();
  }, [props.language]);

  // Replace the document when the value changes from outside (e.g. after a reload).
  useEffect(() => {
    const v = view.current;
    if (v && v.state.doc.toString() !== props.value)
      v.dispatch({ changes: { from: 0, to: v.state.doc.length, insert: props.value } });
  }, [props.value]);

  return <div class="code-editor" ref={host} />;
}

export function Modal(props: { title: string; onClose: () => void; children: ComponentChildren; actions?: ComponentChildren; wide?: boolean }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') props.onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);
  return (
    <div class="modal-backdrop" onClick={(e) => { if (e.target === e.currentTarget) props.onClose(); }}>
      <div class={`modal${props.wide ? ' modal--wide' : ''}`} role="dialog" aria-label={props.title}>
        <header><h2>{props.title}</h2><button class="btn btn--ghost" onClick={props.onClose} title="Close">✕</button></header>
        <div class="modal__body">{props.children}</div>
        {props.actions && <footer>{props.actions}</footer>}
      </div>
    </div>
  );
}

type Toast = { id: number; text: string; kind: 'ok' | 'error' | 'info' };
let pushToast: (t: Omit<Toast, 'id'>) => void = () => {};

export function toast(text: string, kind: Toast['kind'] = 'ok') { pushToast({ text, kind }); }
export function toastError(e: unknown) { toast(e instanceof Error ? e.message : String(e), 'error'); }

export function Toasts() {
  const [items, setItems] = useState<Toast[]>([]);
  pushToast = (t) => {
    const id = Date.now() + Math.random();
    setItems((xs) => [...xs, { ...t, id }]);
    setTimeout(() => setItems((xs) => xs.filter((x) => x.id !== id)), t.kind === 'error' ? 8000 : 3500);
  };
  return <div class="toasts" aria-live="polite">{items.map((t) => <div key={t.id} class={`toast toast--${t.kind}`}>{t.text}</div>)}</div>;
}

/** Runs an async action with a busy flag and error toast. */
export function useAction() {
  const [busy, setBusy] = useState(false);
  const run = async <T,>(fn: () => Promise<T>, success?: string): Promise<T | undefined> => {
    setBusy(true);
    try {
      const result = await fn();
      if (success) toast(success);
      return result;
    } catch (e) {
      toastError(e);
      return undefined;
    } finally {
      setBusy(false);
    }
  };
  return { busy, run };
}

/** Loads data once (and again when `deps` change), exposing a reload function. */
export function useLoad<T>(load: () => Promise<T>, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let cancelled = false;
    load().then((d) => { if (!cancelled) { setData(d); setError(null); } }, (e) => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)); });
    return () => { cancelled = true; };
  }, [...deps, tick]);
  return { data, error, reload: () => setTick((t) => t + 1), setData };
}

export function Loading(props: { error?: string | null }) {
  return props.error ? <p class="error">⚠ {props.error}</p> : <p class="muted">Loading…</p>;
}

export function Field(props: { label: string; hint?: string; children: ComponentChildren }) {
  return (
    <label class="field">
      <span class="field__label">{props.label}</span>
      {props.children}
      {props.hint && <small class="muted">{props.hint}</small>}
    </label>
  );
}

/** Tag-style multi-value input with suggestions. */
export function ChipInput(props: { values: string[]; onChange: (v: string[]) => void; suggestions?: string[]; placeholder?: string }) {
  const [text, setText] = useState('');
  const listId = useRef('dl-' + Math.random().toString(36).slice(2)).current;
  const add = () => {
    const next = [...props.values];
    for (const part of text.split(',').map((s) => s.trim()).filter(Boolean)) {
      const match = props.suggestions?.find((s) => s.toLowerCase() === part.toLowerCase()) ?? part;
      if (!next.includes(match)) next.push(match);
    }
    setText('');
    if (next.length !== props.values.length) props.onChange(next);
  };
  return (
    <div class="chips">
      {props.values.map((v) => (
        <span class="chip" key={v}>{v}<button type="button" title={`Remove ${v}`} onClick={() => props.onChange(props.values.filter((x) => x !== v))}>×</button></span>
      ))}
      <input
        list={listId}
        value={text}
        placeholder={props.placeholder ?? 'Add…'}
        onInput={(e) => setText((e.target as HTMLInputElement).value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ',') { e.preventDefault(); add(); }
          if (e.key === 'Backspace' && !text && props.values.length) props.onChange(props.values.slice(0, -1));
        }}
        onBlur={add}
      />
      <datalist id={listId}>{props.suggestions?.map((s) => <option value={s} key={s} />)}</datalist>
    </div>
  );
}

export function Diff(props: { text: string }) {
  if (!props.text.trim()) return <p class="muted">No differences.</p>;
  return (
    <pre class="diff">
      {props.text.split('\n').map((line, i) => {
        const cls = line.startsWith('+++') || line.startsWith('---') ? 'diff__file'
          : line.startsWith('+') ? 'diff__add' : line.startsWith('-') ? 'diff__del' : line.startsWith('@@') ? 'diff__hunk' : '';
        return <div key={i} class={cls}>{line || ' '}</div>;
      })}
    </pre>
  );
}

export const confirmAction = (message: string) => window.confirm(message);

/** Opens a page in the in-page visual editor. */
