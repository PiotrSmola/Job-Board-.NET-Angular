import { CV_ACCEPT, CV_MAX_SIZE_BYTES, formatFileSize, validateCvFile } from './cv-file';

describe('cv-file', () => {
  it('accepts PDF and DOCX in the file picker', () => {
    expect(CV_ACCEPT).toBe('.pdf,.docx');
  });

  describe('validateCvFile', () => {
    it.each(['cv.pdf', 'CV.PDF', 'moje cv.docx', 'a.b.Docx'])('accepts %s', name => {
      expect(validateCvFile({ name, size: 1000 })).toBeNull();
    });

    it.each(['cv.doc', 'cv.txt', 'cv', 'cv.pdf.exe', 'pdf'])('rejects the extension of %s', name => {
      expect(validateCvFile({ name, size: 1000 })).toBe('Nieobsługiwany typ pliku – wybierz plik PDF lub DOCX');
    });

    it('rejects an empty file', () => {
      expect(validateCvFile({ name: 'cv.pdf', size: 0 })).toBe('Wybrany plik jest pusty');
    });

    it('accepts exactly 5 MB and rejects one byte more', () => {
      expect(validateCvFile({ name: 'cv.pdf', size: CV_MAX_SIZE_BYTES })).toBeNull();
      expect(validateCvFile({ name: 'cv.pdf', size: CV_MAX_SIZE_BYTES + 1 })).toBe('Plik jest za duży – maksymalny rozmiar to 5 MB');
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
});
