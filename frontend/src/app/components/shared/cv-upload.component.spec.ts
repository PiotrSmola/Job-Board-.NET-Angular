import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CvUploadComponent } from './cv-upload.component';

@Component({
  standalone: true,
  imports: [CvUploadComponent],
  template: `<app-cv-upload [(file)]="file" [progress]="progress()" [disabled]="disabled()" [errorMessage]="apiError()" />`
})
class HostComponent {
  readonly file = signal<File | null>(null);
  readonly progress = signal<number | null>(null);
  readonly disabled = signal(false);
  readonly apiError = signal<string | null>(null);
}

function makeFile(name: string, size: number): File {
  const file = new File(['x'], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('CvUploadComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function fileInput(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('input[type="file"]')!;
  }

  function statusText(): string {
    return root().querySelector('.status')?.textContent ?? '';
  }

  /** Symuluje wybór pliku w natywnym oknie (jsdom nie pozwala przypisać `files`). */
  function choose(file: File | null): void {
    const input = fileInput();
    Object.defineProperty(input, 'files', { value: file ? [file] : [], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('accepts only PDF and DOCX in the file picker', () => {
    expect(fileInput().getAttribute('accept')).toBe('.pdf,.docx');
  });

  it('shows the choose button and the hint when no file is selected', () => {
    expect(root().querySelector('.choose-button')?.textContent).toContain('Wybierz plik CV');
    expect(root().querySelector('.hint')?.textContent).toContain('PDF lub DOCX, maks. 5 MB');
    expect(root().querySelector('.selected-file')).toBeNull();
  });

  describe('accessibility', () => {
    it('uses a real, keyboard-focusable file input labelled by the visible choose button', () => {
      const input = fileInput();
      const label = root().querySelector<HTMLLabelElement>('label.choose-button')!;

      expect(input.id).toMatch(/^cv-upload-\d+$/);
      expect(label.htmlFor).toBe(input.id);
      expect(input.hidden).toBe(false);
      expect(input.tabIndex).toBe(0);
      input.focus();
      expect(document.activeElement).toBe(input);
    });

    it('describes the file restrictions through aria-describedby', () => {
      const describedBy = fileInput().getAttribute('aria-describedby')!;

      expect(root().querySelector('#' + describedBy)?.textContent).toContain('PDF lub DOCX, maks. 5 MB');
    });

    it('gives every instance unique ids', () => {
      const other = TestBed.createComponent(HostComponent);
      other.detectChanges();
      const otherInput = (other.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type="file"]')!;

      expect(otherInput.id).not.toBe(fileInput().id);
      expect(otherInput.getAttribute('aria-describedby')).not.toBe(fileInput().getAttribute('aria-describedby'));
    });

    it('announces validation errors inside the described status area', () => {
      choose(makeFile('cv.txt', 10));

      const error = root().querySelector('.error')!;
      expect(error.getAttribute('role')).toBe('alert');
      expect(root().querySelector('#' + fileInput().getAttribute('aria-describedby'))?.contains(error)).toBe(true);
    });

    it('labels the remove button with the file name', () => {
      choose(makeFile('cv.pdf', 10));

      expect(root().querySelector('.remove-button')?.getAttribute('aria-label')).toBe('Usuń wybrany plik: cv.pdf');
    });

    it('exposes the upload progress as an accessible progressbar', () => {
      host.progress.set(40);

      const bar = root().querySelector('mat-progress-bar')!;
      expect(bar.getAttribute('role')).toBe('progressbar');
      expect(bar.getAttribute('aria-valuenow')).toBe('40');
      expect(bar.getAttribute('aria-valuetext')).toBe('Wysyłanie… 40%');
      expect(bar.getAttribute('aria-label')).toBe('Postęp wysyłania CV');
    });

    it('renders hint, error and progress in the same reserved status area (no layout shift)', () => {
      const status = root().querySelector('.status');

      host.progress.set(10);
      expect(root().querySelector('.status')).toBe(status);
      host.progress.set(null);
      choose(makeFile('cv.txt', 1));
      expect(root().querySelector('.status')).toBe(status);
    });
  });

  it('sets a valid file through the two-way binding and shows its name and size', () => {
    const file = makeFile('Jan Kowalski CV.pdf', 350 * 1024);

    choose(file);

    expect(host.file()).toBe(file);
    expect(root().querySelector('.file-name')?.textContent).toContain('Jan Kowalski CV.pdf');
    expect(root().querySelector('.file-size')?.textContent).toContain('350 KB');
    expect(root().querySelector('.choose-button')?.textContent).toContain('Wybierz inny plik');
  });

  it('resets the native input so the same file can be chosen again', () => {
    const input = fileInput();
    const valueSetter = vi.fn();
    Object.defineProperty(input, 'value', { set: valueSetter, get: () => '', configurable: true });

    choose(makeFile('cv.pdf', 10));

    expect(valueSetter).toHaveBeenCalledWith('');
  });

  it.each([
    ['cv.doc', 1000, 'Nieobsługiwany typ pliku – wybierz plik PDF lub DOCX'],
    ['cv.pdf', 5 * 1024 * 1024 + 1, 'Plik jest za duży – maksymalny rozmiar to 5 MB'],
    ['cv.docx', 0, 'Wybrany plik jest pusty']
  ])('rejects %s (%i B) with a Polish message', (name, size, message) => {
    choose(makeFile(name, size));

    expect(host.file()).toBeNull();
    expect(root().querySelector('.error')?.textContent).toContain(message);
    expect(root().querySelector('.hint')).toBeNull();
  });

  it('keeps the previously selected file when a new choice is invalid', () => {
    const valid = makeFile('cv.pdf', 10);
    choose(valid);

    choose(makeFile('cv.exe', 10));

    expect(host.file()).toBe(valid);
    expect(root().querySelector('.error')).not.toBeNull();
  });

  it('clears the error after a valid file is chosen', () => {
    choose(makeFile('cv.txt', 10));
    choose(makeFile('cv.pdf', 10));

    expect(root().querySelector('.error')).toBeNull();
  });

  it('ignores a cancelled picker', () => {
    choose(null);

    expect(host.file()).toBeNull();
    expect(root().querySelector('.error')).toBeNull();
  });

  it('shows an external (API) error in the same announced area', () => {
    host.apiError.set('Plik jest pusty');

    const error = root().querySelector('.error');
    expect(error?.textContent).toContain('Plik jest pusty');
    expect(error?.getAttribute('role')).toBe('alert');
  });

  it('prefers the validation error over the external error', () => {
    host.apiError.set('Błąd API');
    choose(makeFile('cv.txt', 10));

    expect(root().querySelector('.error')?.textContent).toContain('Nieobsługiwany typ pliku');
  });

  it('removes the selected file with the remove button', () => {
    choose(makeFile('cv.pdf', 10));

    root().querySelector<HTMLButtonElement>('.remove-button')!.click();

    expect(host.file()).toBeNull();
    expect(root().querySelector('.choose-button')?.textContent).toContain('Wybierz plik CV');
  });

  it('shows the upload progress and blocks changes while uploading', () => {
    choose(makeFile('cv.pdf', 10));
    host.progress.set(40);

    expect(root().querySelector('mat-progress-bar')).not.toBeNull();
    expect(statusText()).toContain('Wysyłanie… 40%');
    expect(root().querySelector('.remove-button')).toBeNull();
    expect(fileInput().disabled).toBe(true);
  });

  it('shows the progress bar also at 0 %', () => {
    host.progress.set(0);

    expect(statusText()).toContain('Wysyłanie… 0%');
  });

  it('switches to "Przetwarzanie…" when the whole file has been sent', () => {
    host.progress.set(100);

    expect(statusText()).toContain('Przetwarzanie…');
    expect(root().querySelector('mat-progress-bar')?.getAttribute('aria-valuetext')).toBe('Przetwarzanie…');
  });

  it('shows no progress bar when nothing is uploading', () => {
    expect(root().querySelector('mat-progress-bar')).toBeNull();
  });

  it('disables the file input when disabled', () => {
    host.disabled.set(true);

    expect(fileInput().disabled).toBe(true);
    expect(root().querySelector('.choose-button')?.classList).toContain('disabled');
  });

  describe('file size and name edge cases', () => {
    const MAX = 5 * 1024 * 1024;

    it('accepts a file of exactly 5 MB', () => {
      const file = makeFile('cv.pdf', MAX);

      choose(file);

      expect(host.file()).toBe(file);
      expect(root().querySelector('.error')).toBeNull();
      expect(root().querySelector('.file-size')?.textContent).toContain('5 MB');
    });

    it('rejects a file 1 byte over 5 MB', () => {
      choose(makeFile('cv.pdf', MAX + 1));

      expect(host.file()).toBeNull();
      expect(root().querySelector('.error')?.textContent).toContain('Plik jest za duży – maksymalny rozmiar to 5 MB');
    });

    it.each(['CV.PDF', 'Cv.Docx', 'cv.pDf'])('accepts the upper-case extension of %s', name => {
      const file = makeFile(name, 100);

      choose(file);

      expect(host.file()).toBe(file);
      expect(root().querySelector('.error')).toBeNull();
    });

    it.each(['cv', 'pdf', '.hidden', 'cv.', 'cv.pdf.exe', 'cv.docx.txt'])('rejects %s (no valid extension)', name => {
      choose(makeFile(name, 100));

      expect(host.file()).toBeNull();
      expect(root().querySelector('.error')?.textContent).toContain('Nieobsługiwany typ pliku');
    });

    it('treats a cancelled picker (empty file list) as no change and keeps the previous file and error', () => {
      const valid = makeFile('cv.pdf', 10);
      choose(valid);

      choose(null);

      expect(host.file()).toBe(valid);
      expect(root().querySelector('.file-name')?.textContent).toContain('cv.pdf');
      expect(root().querySelector('.error')).toBeNull();
    });

    it('keeps showing a validation error when the picker is cancelled afterwards', () => {
      choose(makeFile('cv.txt', 10));

      choose(null);

      expect(root().querySelector('.error')?.textContent).toContain('Nieobsługiwany typ pliku');
    });

    it('survives a change event without a files list', () => {
      const input = fileInput();
      Object.defineProperty(input, 'files', { value: null, configurable: true });

      input.dispatchEvent(new Event('change'));

      expect(host.file()).toBeNull();
      expect(root().querySelector('.error')).toBeNull();
    });
  });

  describe('security', () => {
    it.each([
      '<script>window.__pwned = true</script>.pdf',
      '<img src=x onerror="window.__pwned = true">.pdf',
      '"><svg onload=alert(1)>.docx',
      '..\\..\\windows\\system32\\evil.pdf',
      '../../etc/passwd.pdf',
      'bell\u0007\u0000null.pdf'
    ])('renders the file name %j as text only', name => {
      choose(makeFile(name, 100));

      const view = root();
      const label = view.querySelector('.file-name')!;
      expect(label.textContent).toBe(name);
      expect(label.children.length).toBe(0);
      expect(view.querySelector('script, img, svg')).toBeNull();
      expect(view.querySelector('.remove-button')?.getAttribute('aria-label')).toBe(`Usuń wybrany plik: ${name}`);
      expect((window as unknown as { __pwned?: boolean }).__pwned).toBeUndefined();
    });

    it('renders an API error containing markup as text only', () => {
      host.apiError.set('<img src=x onerror="window.__pwned = true"> błąd');

      const view = root();
      expect(view.querySelector('.error span')?.textContent).toBe('<img src=x onerror="window.__pwned = true"> błąd');
      expect(view.querySelector('img')).toBeNull();
    });

    it('does not use innerHTML bindings in its template', () => {
      choose(makeFile('<b>x</b>.pdf', 10));

      expect(root().querySelector('.file-name b')).toBeNull();
    });
  });

  describe('accessibility in every state', () => {
    function describedElement(): HTMLElement | null {
      const id = fileInput().getAttribute('aria-describedby');
      return id ? root().querySelector<HTMLElement>('#' + id) : null;
    }

    it('keeps aria-describedby pointing at an existing element in the hint, error and progress states', () => {
      expect(describedElement()?.querySelector('.hint')).not.toBeNull();

      choose(makeFile('cv.txt', 1));
      expect(describedElement()?.querySelector('.error')).not.toBeNull();

      host.progress.set(20);
      expect(describedElement()?.querySelector('mat-progress-bar')).not.toBeNull();

      host.progress.set(null);
      expect(describedElement()).not.toBeNull();
    });

    it('associates the label with the input so the visible text is its accessible name', () => {
      const input = fileInput();

      expect(input.labels?.length).toBe(1);
      expect(input.labels?.[0].textContent).toContain('Wybierz plik CV');
    });

    it('is the only focusable control while empty and keeps the remove button reachable by keyboard afterwards', () => {
      expect(root().querySelectorAll('button').length).toBe(0);

      choose(makeFile('cv.pdf', 10));
      const remove = root().querySelector<HTMLButtonElement>('.remove-button')!;
      remove.focus();

      expect(remove.disabled).toBe(false);
      expect(remove.tabIndex).toBe(0);
      expect(document.activeElement).toBe(remove);
    });

    it('does not hide the file input from assistive technology', () => {
      const input = fileInput();

      expect(input.getAttribute('aria-hidden')).toBeNull();
      expect(input.hidden).toBe(false);
      expect(input.getAttribute('tabindex')).toBeNull();
    });

    it('updates aria-valuenow and aria-valuetext as the progress changes', () => {
      host.progress.set(10);
      const bar = root().querySelector('mat-progress-bar')!;
      expect([bar.getAttribute('aria-valuenow'), bar.getAttribute('aria-valuetext')]).toEqual(['10', 'Wysyłanie… 10%']);

      host.progress.set(70);
      root();
      expect([bar.getAttribute('aria-valuenow'), bar.getAttribute('aria-valuetext')]).toEqual(['70', 'Wysyłanie… 70%']);
      expect(root().querySelector('mat-progress-bar')).toBe(bar);

      host.progress.set(100);
      root();
      expect(bar.getAttribute('aria-valuetext')).toBe('Przetwarzanie…');
    });

    it('hides the visual progress percentage from screen readers, which use the progressbar instead', () => {
      host.progress.set(33);

      expect(root().querySelector('.progress-text')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('marks decorative icons as hidden from assistive technology', () => {
      choose(makeFile('cv.pdf', 10));
      host.apiError.set('Błąd');

      const icons = Array.from(root().querySelectorAll('mat-icon'));
      expect(icons.length).toBeGreaterThan(0);
      expect(icons.every(icon => icon.getAttribute('aria-hidden') === 'true')).toBe(true);
    });

    it('tells the user about the error with an icon and text, not colour alone', () => {
      choose(makeFile('cv.txt', 1));

      const error = root().querySelector('.error')!;
      expect(error.querySelector('mat-icon')?.textContent).toContain('error_outline');
      expect(error.querySelector('span')?.textContent?.trim()).not.toBe('');
    });
  });

  describe('cleanup', () => {
    it('leaves no timers behind after the component is destroyed', () => {
      vi.useFakeTimers();
      try {
        const other = TestBed.createComponent(HostComponent);
        other.componentInstance.progress.set(50);
        other.detectChanges();
        other.componentInstance.progress.set(100);
        other.detectChanges();

        other.destroy();

        expect(vi.getTimerCount()).toBe(0);
      } finally {
        vi.useRealTimers();
      }
    });

    it('does not touch the DOM or emit when inputs change after the component is destroyed', () => {
      const emitted = vi.fn();
      const other = TestBed.createComponent(HostComponent);
      other.detectChanges();
      const picker = (other.nativeElement as HTMLElement).querySelector('app-cv-upload')!;
      const component = other.debugElement.children[0].componentInstance as CvUploadComponent;
      component.fileChange.subscribe(emitted);

      other.destroy();
      other.componentInstance.progress.set(30);
      other.componentInstance.apiError.set('Po zniszczeniu');

      expect(emitted).not.toHaveBeenCalled();
      expect(picker.textContent).not.toContain('Po zniszczeniu');
    });
  });
});
