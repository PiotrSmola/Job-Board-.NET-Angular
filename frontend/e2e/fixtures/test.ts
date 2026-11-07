import { test as base, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { deflateSync } from 'node:zlib';
import { appendFileSync, mkdirSync } from 'node:fs';
import { ARTIFACTS_DIR } from './state';

/** Monitor błędów przeglądarki: konsola (error), wyjątki strony, odpowiedzi 4xx/5xx i nieudane żądania. */
export class BrowserMonitor {
  readonly issues: string[] = [];
  /** COOP jest ignorowany na nie-localhostowym http (stack testowy stoi pod http://frontend) – to nie błąd aplikacji. */
  private readonly allowed: RegExp[] = [/Cross-Origin-Opener-Policy header has been ignored/];

  /** Dopuszcza oczekiwany błąd (dopasowanie do „rodzaj: METODA url status” albo treści komunikatu konsoli). */
  allow(...patterns: RegExp[]): void {
    this.allowed.push(...patterns);
  }

  record(kind: string, text: string): void {
    const line = `${kind}: ${text}`;
    if (this.allowed.some(pattern => pattern.test(line))) return;
    this.issues.push(line);
  }

  attach(page: Page): void {
    page.on('console', message => {
      if (message.type() !== 'error') return;
      const url = message.location().url;
      this.record('console', `${message.text()}${url ? ` [${url}]` : ''}`);
    });
    page.on('pageerror', error => this.record('pageerror', error.message));
    page.on('response', response => {
      if (response.status() >= 400) this.record('http', `${response.request().method()} ${response.url()} ${response.status()}`);
    });
    page.on('requestfailed', request => {
      const failure = request.failure()?.errorText ?? '';
      // przerwane żądania (nawigacja, anulowanie przez aplikację) nie są błędem
      if (failure.includes('ERR_ABORTED')) return;
      this.record('requestfailed', `${request.method()} ${request.url()} ${failure}`);
    });
  }
}

export interface A11yViolation {
  label: string;
  id: string;
  impact: string | null | undefined;
  help: string;
  nodes: string[];
}

export const test = base.extend<{ monitor: BrowserMonitor }>({
  monitor: [async ({ page }, use) => {
    const monitor = new BrowserMonitor();
    monitor.attach(page);
    await use(monitor);
    // soft – nie przesłania innych niepowodzeń testu
    expect.soft(monitor.issues, 'błędy konsoli / żądania 4xx-5xx').toEqual([]);
  }, { auto: true }]
});

export { expect };

/**
 * Skan axe (WCAG 2.0/2.1/2.2 A + AA). Naruszenia `serious` i `critical` kończą test niepowodzeniem,
 * wszystkie trafiają do e2e/.artifacts/a11y.jsonl (podsumowanie do raportu).
 */
export async function scanA11y(page: Page, label: string, options: { exclude?: string[] } = {}): Promise<A11yViolation[]> {
  let builder = new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']);
  for (const selector of options.exclude ?? []) builder = builder.exclude(selector);
  const result = await builder.analyze();
  const violations: A11yViolation[] = result.violations.map(violation => ({
    label,
    id: violation.id,
    impact: violation.impact,
    help: violation.help,
    nodes: violation.nodes.slice(0, 5).map(node => `${node.target.join(' ')} :: ${(node.failureSummary ?? '').split('\n').slice(1, 3).join(' ').trim()}`)
  }));
  mkdirSync(ARTIFACTS_DIR, { recursive: true });
  for (const violation of violations) appendFileSync(`${ARTIFACTS_DIR}/a11y.jsonl`, JSON.stringify(violation) + '\n');
  const blocking = violations.filter(violation => violation.impact === 'serious' || violation.impact === 'critical');
  expect.soft(blocking, `axe (${label}): naruszenia serious/critical`).toEqual([]);
  return violations;
}

/** Unikalny sufiks dla danych utworzonych w teście. */
export const uniqueId = (): string => `${Date.now().toString(36)}${Math.floor(Math.random() * 1296).toString(36)}`;

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

function crc32(buffer: Buffer): number {
  let c = 0xffffffff;
  for (const byte of buffer) c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function pngChunk(type: string, data: Buffer): Buffer {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const checksum = Buffer.alloc(4);
  checksum.writeUInt32BE(crc32(body));
  return Buffer.concat([length, body, checksum]);
}

/** Minimalny poprawny PNG jednego koloru (generowany w locie, bez plików binarnych w repo). */
export function makePng(width: number, height: number, rgb: [number, number, number]): Buffer {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8; // głębia bitowa
  header[9] = 2; // RGB
  const row = Buffer.concat([Buffer.from([0]), Buffer.concat(Array.from({ length: width }, () => Buffer.from(rgb)))]);
  const raw = Buffer.concat(Array.from({ length: height }, () => row));
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    pngChunk('IHDR', header),
    pngChunk('IDAT', deflateSync(raw)),
    pngChunk('IEND', Buffer.alloc(0))
  ]);
}

/** Minimalny PDF (jedna pusta strona) – przechodzi walidację typu po zawartości (%PDF). */
export function makePdf(title: string): Buffer {
  const content = `%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\n% ${title}\ntrailer<</Root 1 0 R>>\n%%EOF\n`;
  return Buffer.from(content, 'latin1');
}

/**
 * Jak `scanA11y`, ale z jawnie włączoną regułą `label-content-name-mismatch` (WCAG 2.5.3 – w axe oznaczona jako eksperymentalna)
 * i bez dopuszczania jej naruszeń na żadnym poziomie (impact), nie tylko serious/critical.
 */
export async function scanA11yStrict(page: Page, label: string, options: { exclude?: string[] } = {}): Promise<A11yViolation[]> {
  let builder = new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .options({ rules: { 'label-content-name-mismatch': { enabled: true } } });
  for (const selector of options.exclude ?? []) builder = builder.exclude(selector);
  const result = await builder.analyze();
  const violations: A11yViolation[] = result.violations.map(violation => ({
    label,
    id: violation.id,
    impact: violation.impact,
    help: violation.help,
    nodes: violation.nodes.slice(0, 5).map(node => `${node.target.join(' ')} :: ${(node.failureSummary ?? '').split('\n').slice(1, 3).join(' ').trim()}`)
  }));
  mkdirSync(ARTIFACTS_DIR, { recursive: true });
  for (const violation of violations) appendFileSync(`${ARTIFACTS_DIR}/a11y.jsonl`, JSON.stringify(violation) + '\n');
  const blocking = violations.filter(violation =>
    violation.impact === 'serious' || violation.impact === 'critical' || violation.id === 'label-content-name-mismatch');
  expect.soft(blocking, `axe (${label}): serious/critical i label-content-name-mismatch`).toEqual([]);
  return violations;
}
