import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

const STATE_FILE = resolve(__dirname, '..', '.state', 'state.json');
export const AUTH_DIR = resolve(__dirname, '..', '.auth');
export const ARTIFACTS_DIR = resolve(__dirname, '..', '.artifacts');

export const authFile = (role: 'candidate' | 'employer' | 'admin') => resolve(AUTH_DIR, `${role}.json`);

export const EMPLOYER = { email: 'rekruter@techcorp.pl', password: 'Employer123!' };
export const ADMIN = { email: 'admin@jobboard.com', password: 'Admin123!' };

/** Dane wymieniane między plikami scenariuszy (konto kandydata, oferta, aplikacja). */
export interface E2eState {
  candidate?: { email: string; password: string; firstName: string; lastName: string };
  /** Oferta utworzona przez pracodawcę w teście (do sprzątania). */
  offer?: { id?: number; title: string };
  /** Oferta, na którą aplikował kandydat. */
  appliedJob?: { id: number; title: string };
  /** Identyfikator uruchomienia (sufiks nazw danych testowych). */
  runId?: string;
}

export function readState(): E2eState {
  if (!existsSync(STATE_FILE)) return {};
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as E2eState;
}

export function writeState(patch: Partial<E2eState>): E2eState {
  const next = { ...readState(), ...patch };
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(next, null, 2));
  return next;
}
