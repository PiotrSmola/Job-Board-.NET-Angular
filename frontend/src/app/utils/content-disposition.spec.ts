import { getFileNameFromContentDisposition, resolveDownloadFileName } from './content-disposition';

describe('content-disposition', () => {
  describe('getFileNameFromContentDisposition', () => {
    it('decodes the UTF-8 filename* parameter', () => {
      const header = "attachment; filename*=UTF-8''%C5%BC%C3%B3%C5%82w%20CV.pdf";
      expect(getFileNameFromContentDisposition(header)).toBe('żółw CV.pdf');
    });

    it('prefers filename* over filename when ASP.NET sends both', () => {
      const header = "attachment; filename=zolw.pdf; filename*=UTF-8''%C5%BC%C3%B3%C5%82w.pdf";
      expect(getFileNameFromContentDisposition(header)).toBe('żółw.pdf');
    });

    it('accepts a lowercase charset and a language tag', () => {
      expect(getFileNameFromContentDisposition("attachment; filename*=utf-8'pl'cv.docx")).toBe('cv.docx');
    });

    it('decodes ISO-8859-1 filename*', () => {
      expect(getFileNameFromContentDisposition("attachment; filename*=ISO-8859-1''r%E9sum%E9.pdf")).toBe('résumé.pdf');
    });

    it('falls back to filename when filename* has an invalid percent sequence', () => {
      const header = "attachment; filename=\"cv.pdf\"; filename*=UTF-8''%E0%A4%A.pdf";
      expect(getFileNameFromContentDisposition(header)).toBe('cv.pdf');
    });

    it('falls back to filename when filename* has an unknown charset', () => {
      expect(getFileNameFromContentDisposition("attachment; filename*=KOI8-R''x.pdf; filename=cv.pdf")).toBe('cv.pdf');
    });

    it('reads a quoted filename with escaped characters', () => {
      expect(getFileNameFromContentDisposition('attachment; filename="moje \\"CV\\"; 2026.pdf"')).toBe('moje _CV_; 2026.pdf');
    });

    it('replaces characters reserved in Windows file names', () => {
      expect(getFileNameFromContentDisposition("attachment; filename*=UTF-8''a%3Cb%3E%3Ac%7Cd%3F%2A.pdf")).toBe('a_b__c_d__.pdf');
    });

    it('strips path separators decoded from filename*', () => {
      expect(getFileNameFromContentDisposition("attachment; filename*=UTF-8''..%2F..%2Fetc%5Cevil.pdf")).toBe('evil.pdf');
    });

    it('reads an unquoted filename', () => {
      expect(getFileNameFromContentDisposition('attachment; filename=cv.pdf')).toBe('cv.pdf');
    });

    it('is case-insensitive for parameter names', () => {
      expect(getFileNameFromContentDisposition('attachment; FileName="cv.pdf"')).toBe('cv.pdf');
    });

    it('strips path segments and control characters', () => {
      expect(getFileNameFromContentDisposition('attachment; filename="..\\..\\etc/pass\u0007wd.pdf"')).toBe('passwd.pdf');
    });

    it.each([null, undefined, '', 'attachment', 'inline; filename=""', "attachment; filename*=UTF-8''", 'attachment; filename=".."'])(
      'returns null for %j',
      header => {
        expect(getFileNameFromContentDisposition(header)).toBeNull();
      }
    );
  });

  describe('resolveDownloadFileName', () => {
    it('uses the header when it contains a file name', () => {
      expect(resolveDownloadFileName('attachment; filename=a.pdf', 'b.pdf')).toBe('a.pdf');
    });

    it('falls back to the given name (attachment fileName from the DTO) when the header is missing', () => {
      expect(resolveDownloadFileName(null, 'moje-cv.docx')).toBe('moje-cv.docx');
    });

    it('sanitizes the fallback name', () => {
      expect(resolveDownloadFileName(null, 'C:\\Users\\jan\\cv.pdf')).toBe('cv.pdf');
    });

    it('uses the default name when nothing else is available', () => {
      expect(resolveDownloadFileName(null, null)).toBe('zalacznik');
      expect(resolveDownloadFileName('attachment', '  ', 'plik')).toBe('plik');
    });
  });
});
