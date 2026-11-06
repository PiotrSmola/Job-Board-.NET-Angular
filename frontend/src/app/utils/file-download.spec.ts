import { saveBlobAsFile } from './file-download';

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

  it('revokes the object URL after the download has started', () => {
    saveBlobAsFile(new Blob(['x']), 'cv.pdf');
    expect(revokeObjectURL).not.toHaveBeenCalled();

    vi.runAllTimers();

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:http://localhost/abc');
  });
});
