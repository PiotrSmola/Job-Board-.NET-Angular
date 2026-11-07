import { APIRequestContext } from '@playwright/test';

const MAILPIT_URL = process.env['E2E_MAILPIT_URL'] ?? 'http://mailpit:8025';

interface MailpitMessage { ID: string; Subject: string; Created: string }

/** Czeka na wiadomość do adresu (opcjonalnie z fragmentem tematu) i zwraca jej treść tekstową. */
export async function waitForMail(request: APIRequestContext, to: string, subjectPart = '', timeoutMs = 20_000): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const response = await request.get(`${MAILPIT_URL}/api/v1/search`, { params: { query: `to:${to}` } });
    if (response.ok()) {
      const body = await response.json() as { messages: MailpitMessage[] };
      const found = body.messages.find(message => message.Subject.includes(subjectPart));
      if (found) {
        const detail = await request.get(`${MAILPIT_URL}/api/v1/message/${found.ID}`);
        return (await detail.json() as { Text: string }).Text;
      }
    }
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new Error(`Brak wiadomości do ${to} (temat zawiera „${subjectPart}”) w Mailpit po ${timeoutMs} ms`);
}

/** Link z treści maila prowadzący do podanej ścieżki – zwracany jako ścieżka + query (host z maila to localhost:4200). */
export function extractAppLink(text: string, path: string): string {
  const match = text.match(new RegExp(`https?://[^\\s]+?(${path}[^\\s]*)`));
  if (!match) throw new Error(`W wiadomości nie ma linku do ${path}`);
  return match[1];
}
