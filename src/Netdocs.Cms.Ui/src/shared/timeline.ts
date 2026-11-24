// The YAML inside a ```timeline fence (the timeline plugin), as a form can edit it.
// Only the plugin's own shape is read: top-level scalars, `inputs`/`outputs` lists of flat maps
// and `exclusions` as dates or `{ from, to }` ranges. Anything else (comments, unknown keys,
// other YAML forms) returns null so the block opens in the code editor and nothing is lost.

export interface TimelineInput { name: string; label?: string; default: string; type?: string; duration?: string; editable?: boolean }
export interface TimelineOutput { name: string; label?: string; type?: string; expr: string; duration?: string }
export type TimelineExclusion = string | { from: string; to: string };

export interface Timeline {
  title?: string;
  date_format?: string;
  display_date_format?: string;
  show_summary?: boolean;
  edit_exclusions?: boolean;
  exclusions?: TimelineExclusion[];
  inputs: TimelineInput[];
  outputs: TimelineOutput[];
}

const scalarKeys = ['title', 'date_format', 'display_date_format'] as const;
const boolKeys = ['show_summary', 'edit_exclusions'] as const;
const inputKeys = ['name', 'label', 'default', 'type', 'duration', 'editable'];
const outputKeys = ['name', 'label', 'type', 'expr', 'duration'];

function unquote(v: string): string | null {
  v = v.trim();
  if (/^"(?:[^"\\]|\\.)*"$/.test(v)) { try { return JSON.parse(v) as string; } catch { return null; } }
  if (/^'(?:[^']|'')*'$/.test(v)) return v.slice(1, -1).replace(/''/g, "'");
  if (/^["'{[&*!|>%@`]/.test(v) || /\s#/.test(v)) return null;
  return v;
}

function bool(v: string): boolean | null {
  const s = v.trim().toLowerCase();
  return s === 'true' ? true : s === 'false' ? false : null;
}

/** Reads `key: value` from a line, or null. */
function pair(line: string): [string, string] | null {
  const m = /^([A-Za-z_][A-Za-z0-9_]*):(?:\s+(.*))?$/.exec(line);
  return m ? [m[1], m[2] ?? ''] : null;
}

export function parseTimeline(code: string): Timeline | null {
  const t: Timeline = { inputs: [], outputs: [] };
  const seen = new Set<string>();
  let list: 'inputs' | 'outputs' | 'exclusions' | null = null;
  let item: Record<string, string> | null = null;
  let itemIndent = 0;
  const lists: Record<'inputs' | 'outputs', Record<string, string>[]> = { inputs: [], outputs: [] };

  for (const raw of code.split('\n')) {
    if (!raw.trim()) continue;
    if (/^\s*#/.test(raw) || raw.includes('\t')) return null;
    const indent = raw.length - raw.trimStart().length;
    const line = raw.trim();

    if (indent === 0) {
      item = null;
      const kv = pair(line);
      if (!kv || seen.has(kv[0])) return null;
      const [key, value] = kv;
      seen.add(key);
      if (key === 'inputs' || key === 'outputs' || key === 'exclusions') {
        if (value.trim() === '[]') { list = null; if (key === 'exclusions') t.exclusions = []; continue; }
        if (value.trim()) return null;
        list = key;
        if (key === 'exclusions') t.exclusions = [];
        continue;
      }
      list = null;
      if ((scalarKeys as readonly string[]).includes(key)) {
        const v = unquote(value);
        if (v === null) return null;
        t[key as (typeof scalarKeys)[number]] = v;
      } else if ((boolKeys as readonly string[]).includes(key)) {
        const b = bool(value);
        if (b === null) return null;
        t[key as (typeof boolKeys)[number]] = b;
      } else return null;
      continue;
    }

    if (!list) return null;
    const dash = /^-\s*(.*)$/.exec(line);
    if (list === 'exclusions') {
      if (!dash) return null;
      const range = /^\{\s*from:\s*([^,}]+?)\s*,\s*to:\s*([^,}]+?)\s*\}$/.exec(dash[1]);
      if (range) {
        const from = unquote(range[1]), to = unquote(range[2]);
        if (from === null || to === null) return null;
        t.exclusions!.push({ from, to });
      } else {
        const v = unquote(dash[1]);
        if (v === null || !v) return null;
        t.exclusions!.push(v);
      }
      continue;
    }

    let body = line;
    if (dash) {
      item = {};
      lists[list].push(item);
      itemIndent = indent + 2;
      body = dash[1];
      if (!body) continue;
    } else if (!item || indent !== itemIndent) return null;
    const kv = pair(body);
    if (!kv || kv[0] in item) return null;
    const allowed = list === 'inputs' ? inputKeys : outputKeys;
    if (!allowed.includes(kv[0])) return null;
    const v = unquote(kv[1]);
    if (v === null) return null;
    item[kv[0]] = v;
  }

  for (const r of lists.inputs) {
    if (r.editable !== undefined && bool(r.editable) === null) return null;
    t.inputs.push({
      name: r.name ?? '', label: r.label, default: r.default ?? '', type: r.type, duration: r.duration,
      editable: r.editable === undefined ? undefined : bool(r.editable)!,
    });
  }
  for (const r of lists.outputs) {
    t.outputs.push({ name: r.name ?? '', label: r.label, type: r.type, expr: r.expr ?? '', duration: r.duration });
  }
  return t;
}

function scalar(v: string): string {
  if (v === '' || /^[\s]|[\s]$|^[-?:,[\]{}#&*!|>'"%@`]|: | #|^(true|false|null|yes|no|on|off|~)$/i.test(v) || /^[\d.+-]+$/.test(v) && !/^\d+$/.test(v)) {
    return JSON.stringify(v);
  }
  return v;
}

export function formatTimeline(t: Timeline): string {
  const out: string[] = [];
  for (const k of scalarKeys) if (t[k] !== undefined && t[k] !== '') out.push(`${k}: ${scalar(t[k]!)}`);
  for (const k of boolKeys) if (t[k] !== undefined) out.push(`${k}: ${t[k]}`);
  const map = (fields: [string, string | boolean | undefined][]) => {
    const kept = fields.filter(([, v]) => v !== undefined && v !== '');
    return kept.map(([k, v], i) => `${i ? '    ' : '  - '}${k}: ${typeof v === 'boolean' ? v : scalar(v as string)}`);
  };
  if (t.inputs.length) {
    out.push('inputs:');
    for (const i of t.inputs) out.push(...map([['name', i.name], ['label', i.label], ['default', i.default], ['type', i.type], ['duration', i.duration], ['editable', i.editable]]));
  }
  if (t.exclusions && t.exclusions.length) {
    out.push('exclusions:');
    for (const e of t.exclusions) out.push(typeof e === 'string' ? `  - ${scalar(e)}` : `  - { from: ${scalar(e.from)}, to: ${scalar(e.to)} }`);
  }
  if (t.outputs.length) {
    out.push('outputs:');
    for (const o of t.outputs) out.push(...map([['name', o.name], ['label', o.label], ['type', o.type], ['expr', o.expr], ['duration', o.duration]]));
  }
  return out.join('\n');
}

/** Splits `start + 5` into its parts; null when it is not that shape. */
export function parseExpr(expr: string): { ref: string; sign: '+' | '-'; count: string } | null {
  const m = /^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:([+-])\s*(\d+))?\s*$/.exec(expr);
  return m ? { ref: m[1], sign: (m[2] as '+' | '-') ?? '+', count: m[3] ?? '' } : null;
}

export function formatExpr(ref: string, sign: string, count: string): string {
  const n = count.trim();
  return n && n !== '0' ? `${ref} ${sign} ${n}` : ref;
}

/** A date written in `format` (tokens YYYY, YY, MM, M, DD, D) as yyyy-mm-dd, or '' when it does not parse. */
export function toIsoDate(value: string, format: string): string {
  const tokens: string[] = [];
  const pattern = format.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/YYYY|YY|MM|M|DD|D/g, (tok) => {
    tokens.push(tok);
    return tok === 'YYYY' ? '(\\d{4})' : tok.length === 2 ? '(\\d{2})' : '(\\d{1,2})';
  });
  const m = new RegExp(`^${pattern}$`).exec(value.trim());
  if (!m) return '';
  let y = 0, mo = 0, d = 0;
  tokens.forEach((tok, i) => {
    const n = Number(m[i + 1]);
    if (tok.startsWith('Y')) y = tok === 'YY' ? 2000 + n : n;
    else if (tok.startsWith('M')) mo = n;
    else d = n;
  });
  if (!y || mo < 1 || mo > 12 || d < 1 || d > 31) return '';
  return `${String(y).padStart(4, '0')}-${String(mo).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
}

/** A yyyy-mm-dd date written in `format`. */
export function fromIsoDate(iso: string, format: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!m) return '';
  const [y, mo, d] = [m[1], m[2], m[3]];
  return format.replace(/YYYY|YY|MM|M|DD|D/g, (tok) => ({
    YYYY: y, YY: y.slice(2), MM: mo, M: String(Number(mo)), DD: d, D: String(Number(d)),
  })[tok]!);
}

/** Whether `format` only uses tokens the date picker can round-trip. */
export function pickerFormat(format: string): boolean {
  return /YY/.test(format) && /M/.test(format) && /D/.test(format) && !/[A-Za-z]/.test(format.replace(/YYYY|YY|MM|M|DD|D/g, ''));
}
