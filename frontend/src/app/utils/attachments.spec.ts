import { AttachmentKind } from '../models/job-application.model';
import {
  ATTACHMENT_ACCEPT,
  ATTACHMENT_KIND_OPTIONS,
  ATTACHMENT_MAX_FILE_SIZE,
  ATTACHMENTS_MAX_COUNT,
  ATTACHMENTS_MAX_TOTAL_SIZE,
  defaultAttachmentKind,
  formatAttachmentsUsage,
  formatFileSize,
  getAttachmentKindLabel,
  selectAttachmentFiles,
  validateAttachmentFile
} from './attachments';

function makeFile(name: string, size: number, lastModified = 1): File {
  const file = new File(['x'], name, { lastModified });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

const MB = 1024 * 1024;

describe('attachments', () => {
  it('accepts PDF, DOCX, PNG and JPEG in the file picker', () => {
    expect(ATTACHMENT_ACCEPT).toBe('.pdf,.docx,.png,.jpg,.jpeg');
  });

  it('uses the limits of the API: 5 MB per file, 25 MB and 10 files per application', () => {
    expect(ATTACHMENT_MAX_FILE_SIZE).toBe(5_242_880);
    expect(ATTACHMENTS_MAX_TOTAL_SIZE).toBe(26_214_400);
    expect(ATTACHMENTS_MAX_COUNT).toBe(10);
  });

  describe('kinds', () => {
    it.each([
      [AttachmentKind.Cv, 'CV'],
      [AttachmentKind.CoverLetter, 'List motywacyjny'],
      [AttachmentKind.Portfolio, 'Portfolio'],
      [AttachmentKind.Other, 'Inny']
    ])('labels kind %i as %s', (kind, label) => {
      expect(getAttachmentKindLabel(kind)).toBe(label);
    });

    it('labels an unknown kind (newer API) as "Inny"', () => {
      expect(getAttachmentKindLabel(42 as AttachmentKind)).toBe('Inny');
    });

    it('offers all kinds in the API order', () => {
      expect(ATTACHMENT_KIND_OPTIONS.map(o => o.value)).toEqual([0, 1, 2, 3]);
      expect(ATTACHMENT_KIND_OPTIONS.map(o => o.label)).toEqual(['CV', 'List motywacyjny', 'Portfolio', 'Inny']);
    });

    it('suggests CV until the application has one, then "Inny"', () => {
      expect(defaultAttachmentKind([])).toBe(AttachmentKind.Cv);
      expect(defaultAttachmentKind([AttachmentKind.Portfolio])).toBe(AttachmentKind.Cv);
      expect(defaultAttachmentKind([AttachmentKind.Cv])).toBe(AttachmentKind.Other);
    });
  });

  describe('validateAttachmentFile', () => {
    it.each(['cv.pdf', 'CV.PDF', 'moje cv.docx', 'a.b.Docx', 'portfolio.png', 'zdjęcie.JPG', 'skan.jpeg'])('accepts %s', name => {
      expect(validateAttachmentFile({ name, size: 1000 })).toBeNull();
    });

    it.each(['cv.doc', 'cv.txt', 'cv', 'cv.pdf.exe', 'pdf', 'obraz.gif', 'obraz.svg', 'x.jpeg.html'])('rejects the extension of %s', name => {
      expect(validateAttachmentFile({ name, size: 1000 })).toBe('Nieobsługiwany typ pliku – wybierz plik PDF, DOCX, PNG lub JPEG');
    });

    it('rejects an empty file', () => {
      expect(validateAttachmentFile({ name: 'cv.pdf', size: 0 })).toBe('Wybrany plik jest pusty');
    });

    it('accepts exactly 5 MB and rejects one byte more', () => {
      expect(validateAttachmentFile({ name: 'cv.pdf', size: ATTACHMENT_MAX_FILE_SIZE })).toBeNull();
      expect(validateAttachmentFile({ name: 'cv.pdf', size: ATTACHMENT_MAX_FILE_SIZE + 1 }))
        .toBe('Plik jest za duży – maksymalny rozmiar to 5 MB');
    });
  });

  describe('selectAttachmentFiles', () => {
    const EMPTY = { count: 0, totalSize: 0 };

    it('accepts valid files and reports the invalid ones with their names', () => {
      const pdf = makeFile('cv.pdf', 100);
      const png = makeFile('portfolio.png', 100);

      const result = selectAttachmentFiles([pdf, makeFile('wirus.exe', 100), png, makeFile('pusty.docx', 0)], EMPTY);

      expect(result.accepted).toEqual([pdf, png]);
      expect(result.errors).toEqual([
        'wirus.exe: Nieobsługiwany typ pliku – wybierz plik PDF, DOCX, PNG lub JPEG',
        'pusty.docx: Wybrany plik jest pusty'
      ]);
    });

    it('counts the existing and already selected files towards the 25 MB total', () => {
      // 20 MB na serwerze + 4 MB wybrane: zmieści się jeszcze 1 MB
      const fits = makeFile('a.pdf', 1 * MB);
      const tooBig = makeFile('b.pdf', 1);

      const result = selectAttachmentFiles([fits, tooBig], { count: 3, totalSize: 24 * MB });

      expect(result.accepted).toEqual([fits]);
      expect(result.errors).toEqual(['b.pdf: Łączny rozmiar załączników nie może przekroczyć 25 MB']);
    });

    it('accepts files that sum up to exactly 25 MB', () => {
      const files = Array.from({ length: 5 }, (_, i) => makeFile(`plik${i}.pdf`, 5 * MB));

      const result = selectAttachmentFiles(files, EMPTY);

      expect(result.accepted.length).toBe(5);
      expect(result.errors).toEqual([]);
    });

    it('skips a big file but still accepts a later smaller one that fits', () => {
      const small = makeFile('maly.pdf', 1 * MB);

      const result = selectAttachmentFiles([makeFile('duzy.pdf', 5 * MB), small], { count: 0, totalSize: 22 * MB });

      expect(result.accepted).toEqual([small]);
      expect(result.errors.length).toBe(1);
    });

    it('limits the number of attachments to 10 including the existing ones', () => {
      const files = Array.from({ length: 4 }, (_, i) => makeFile(`p${i}.pdf`, 10));

      const result = selectAttachmentFiles(files, { count: 8, totalSize: 0 });

      expect(result.accepted).toEqual(files.slice(0, 2));
      expect(result.errors).toEqual([
        'p2.pdf: Aplikacja może mieć maksymalnie 10 załączników',
        'p3.pdf: Aplikacja może mieć maksymalnie 10 załączników'
      ]);
    });

    it('rejects every file when the limit of 10 is already reached', () => {
      const result = selectAttachmentFiles([makeFile('a.pdf', 10)], { count: 10, totalSize: 0 });

      expect(result.accepted).toEqual([]);
      expect(result.errors[0]).toContain('maksymalnie 10 załączników');
    });

    it('does not add the same file twice (same name, size and modification time)', () => {
      const listed = makeFile('cv.pdf', 100, 5);
      const again = makeFile('cv.pdf', 100, 5);
      const other = makeFile('cv.pdf', 100, 6);

      const result = selectAttachmentFiles([again, other, other], { count: 1, totalSize: 100, files: [listed] });

      expect(result.accepted).toEqual([other]);
      expect(result.errors).toEqual(['cv.pdf: Ten plik jest już na liście', 'cv.pdf: Ten plik jest już na liście']);
    });

    it('validates the type before the limits', () => {
      const result = selectAttachmentFiles([makeFile('a.exe', 10)], { count: 10, totalSize: 26 * MB });

      expect(result.errors).toEqual(['a.exe: Nieobsługiwany typ pliku – wybierz plik PDF, DOCX, PNG lub JPEG']);
    });
  });

  describe('formatFileSize', () => {
    it.each([
      [0, '0 B'],
      [512, '512 B'],
      [2048, '2 KB'],
      [350 * 1024, '350 KB'],
      [1024 * 1024, '1 MB'],
      [1.25 * 1024 * 1024, '1,3 MB'],
      [5 * 1024 * 1024, '5 MB']
    ])('formats %i bytes as %s', (bytes, expected) => {
      expect(formatFileSize(bytes)).toBe(expected);
    });

    it.each([null, undefined, -1, Number.NaN])('returns an empty string for %s', bytes => {
      expect(formatFileSize(bytes)).toBe('');
    });
  });

  describe('formatAttachmentsUsage', () => {
    it.each([
      [0, 'Wykorzystano 0 MB z 25 MB'],
      [3.5 * MB, 'Wykorzystano 3,5 MB z 25 MB'],
      [25 * MB, 'Wykorzystano 25 MB z 25 MB']
    ])('describes %i bytes as "%s"', (bytes, expected) => {
      expect(formatAttachmentsUsage(bytes)).toBe(expected);
    });
  });
});
