import { OBJECT_URL_REVOKE_DELAY_MS, saveBlobAsFile } from './file-download';

describe('saveBlobAsFile', () => {
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;
  let clicked: HTMLAnchorElement[];

  beforeEach(() => {
    vi.useFakeTimers();
    // jsdom nie implementuje URL.createObjectURL
    createObjectURL = vi.fn().mockReturnValue('blob:http://localhost/abc');
    revokeObjectURL = vi.fn();
    Object.assign(URL, { createObjectURL, revokeObjectURL });
    clicked = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicked.push(this);
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('clicks a temporary link with the object URL and the file name', () => {
    const blob = new Blob(['%PDF-'], { type: 'application/pdf' });

    saveBlobAsFile(blob, 'żółw CV.pdf');

    expect(createObjectURL).toHaveBeenCalledWith(blob);
    expect(clicked.length).toBe(1);
    expect(clicked[0].href).toBe('blob:http://localhost/abc');
    expect(clicked[0].download).toBe('żółw CV.pdf');
  });

  it('removes the link from the document', () => {
    saveBlobAsFile(new Blob(['x']), 'cv.pdf');

    expect(document.querySelector('a[download]')).toBeNull();
  });

  it('revokes the object URL about 30 s after the download has started (not in the next tick)', () => {
    saveBlobAsFile(new Blob(['x']), 'cv.pdf');
    expect(OBJECT_URL_REVOKE_DELAY_MS).toBe(30_000);

    vi.advanceTimersByTime(0);
    expect(revokeObjectURL).not.toHaveBeenCalled();

    vi.advanceTimersByTime(29_999);
    expect(revokeObjectURL).not.toHaveBeenCalled();

    vi.advanceTimersByTime(1);
    expect(revokeObjectURL).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:http://localhost/abc');
    expect(vi.getTimerCount()).toBe(0);
  });

  it('revokes every created object URL exactly once and leaves no pending timers', () => {
    let counter = 0;
    createObjectURL.mockImplementation(() => `blob:http://localhost/${++counter}`);

    saveBlobAsFile(new Blob(['a']), 'a.pdf');
    saveBlobAsFile(new Blob(['b']), 'b.pdf');
    vi.runAllTimers();

    expect(revokeObjectURL.mock.calls.map(call => call[0])).toEqual(['blob:http://localhost/1', 'blob:http://localhost/2']);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('still revokes the object URL and removes the link when the click throws', () => {
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {
      throw new Error('download blocked');
    });

    expect(() => saveBlobAsFile(new Blob(['x']), 'cv.pdf')).toThrow('download blocked');
    vi.runAllTimers();

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:http://localhost/abc');
    expect(document.querySelector('a[download]')).toBeNull();
  });

  it('uses the file name as plain text of the download attribute (no markup injected into the page)', () => {
    saveBlobAsFile(new Blob(['x']), '<img src=x onerror=alert(1)>.pdf');

    expect(clicked[0].download).toBe('<img src=x onerror=alert(1)>.pdf');
    expect(clicked[0].innerHTML).toBe('');
    expect(document.querySelector('img')).toBeNull();
  });

  it('does not set a target, so the blob is never opened in a window that could run it', () => {
    saveBlobAsFile(new Blob(['x']), 'cv.pdf');

    expect(clicked[0].hasAttribute('target')).toBe(false);
    expect(clicked[0].rel).toBe('noopener');
  });
});
