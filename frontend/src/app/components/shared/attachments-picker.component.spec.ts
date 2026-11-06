import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AttachmentsPickerComponent } from './attachments-picker.component';
import { ApplicationAttachment, AttachmentKind, AttachmentUploadItem } from '../../models/job-application.model';

@Component({
  standalone: true,
  imports: [AttachmentsPickerComponent],
  template: `
    <app-attachments-picker
      [(items)]="items"
      [existing]="existing()"
      [progress]="progress()"
      [disabled]="disabled()"
      [errorMessage]="apiError()"
      [statusMessage]="statusMessage()" />
  `
})
class HostComponent {
  readonly items = signal<AttachmentUploadItem[]>([]);
  readonly existing = signal<ApplicationAttachment[]>([]);
  readonly progress = signal<number | null>(null);
  readonly disabled = signal(false);
  readonly apiError = signal<string | null>(null);
  readonly statusMessage = signal<string | null>(null);
}

const MB = 1024 * 1024;

function makeFile(name: string, size: number): File {
  const file = new File(['x'], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

function existing(count: number, size = 1024, kind = AttachmentKind.Other): ApplicationAttachment[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1, kind, fileName: `istniejacy-${i}.pdf`, size, uploadedAt: '2026-01-02T10:00:00Z'
  }));
}

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AttachmentsPickerComponent', () => {
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

  function liveStatus(): HTMLElement {
    return root().querySelector<HTMLElement>('[role="status"]')!;
  }

  function names(): string[] {
    return Array.from(root().querySelectorAll('.file-name')).map(e => e.textContent ?? '');
  }

  function selects(): HTMLSelectElement[] {
    return Array.from(root().querySelectorAll<HTMLSelectElement>('select.kind-select'));
  }

  function removeButtons(): HTMLButtonElement[] {
    return Array.from(root().querySelectorAll<HTMLButtonElement>('.remove-button'));
  }

  /** Symuluje wybór plików w natywnym oknie (jsdom nie pozwala przypisać `files`). */
  function choose(...files: File[]): void {
    const input = fileInput();
    Object.defineProperty(input, 'files', { value: files, configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  /** Nazwa dostępna elementu wskazanego przez aria-labelledby (teksty wskazanych elementów). */
  function labelledByText(element: HTMLElement): string {
    return (element.getAttribute('aria-labelledby') ?? '').split(' ')
      .map(id => root().querySelector('#' + id)?.textContent?.trim() ?? '<missing>')
      .join(' ');
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('accepts PDF, DOCX, PNG and JPEG and allows choosing many files at once', () => {
    expect(fileInput().getAttribute('accept')).toBe('.pdf,.docx,.png,.jpg,.jpeg');
    expect(fileInput().multiple).toBe(true);
  });

  it('shows the choose button, the hint and the usage counter when nothing is selected', () => {
    expect(root().querySelector('.choose-button')?.textContent).toContain('Wybierz pliki');
    expect(root().querySelector('.hint')?.textContent).toContain('PDF, DOCX, PNG lub JPEG – maks. 5 MB na plik');
    expect(root().querySelector('.usage')?.textContent).toBe('Wykorzystano 0 MB z 25 MB · plików: 0 z 10');
    expect(root().querySelector('.files')).toBeNull();
  });

  it('sets valid files through the two-way binding and lists their names and sizes', () => {
    const cv = makeFile('Jan Kowalski CV.pdf', 350 * 1024);
    const portfolio = makeFile('portfolio.png', 2 * MB);

    choose(cv, portfolio);

    expect(host.items().map(i => i.file)).toEqual([cv, portfolio]);
    expect(names()).toEqual(['Jan Kowalski CV.pdf', 'portfolio.png']);
    expect(Array.from(root().querySelectorAll('.file-size')).map(e => e.textContent)).toEqual(['350 KB', '2 MB']);
    expect(root().querySelector('.choose-button')?.textContent).toContain('Dodaj kolejne pliki');
  });

  it('appends a later selection to the files already on the list', () => {
    choose(makeFile('a.pdf', 10));
    choose(makeFile('b.docx', 10));

    expect(names()).toEqual(['a.pdf', 'b.docx']);
  });

  it('resets the native input so the same file can be chosen again', () => {
    const input = fileInput();
    const valueSetter = vi.fn();
    Object.defineProperty(input, 'value', { set: valueSetter, get: () => '', configurable: true });

    choose(makeFile('cv.pdf', 10));

    expect(valueSetter).toHaveBeenCalledWith('');
  });

  describe('kind of each file', () => {
    it('suggests CV for the first file and "Inny" for the next ones', () => {
      choose(makeFile('cv.pdf', 10), makeFile('list.docx', 10), makeFile('foto.jpg', 10));

      expect(host.items().map(i => i.kind)).toEqual([AttachmentKind.Cv, AttachmentKind.Other, AttachmentKind.Other]);
      expect(selects().map(s => s.value)).toEqual(['0', '3', '3']);
    });

    it('does not suggest a second CV when the application already has one', () => {
      host.existing.set(existing(1, 1024, AttachmentKind.Cv));

      choose(makeFile('nowy.pdf', 10));

      expect(host.items()[0].kind).toBe(AttachmentKind.Other);
    });

    it('offers all kinds with Polish labels', () => {
      choose(makeFile('cv.pdf', 10));

      const options = Array.from(selects()[0].options).map(o => [o.value, o.textContent?.trim()]);
      expect(options).toEqual([['0', 'CV'], ['1', 'List motywacyjny'], ['2', 'Portfolio'], ['3', 'Inny']]);
    });

    it('changes the kind of one file through the select', () => {
      choose(makeFile('cv.pdf', 10), makeFile('list.docx', 10));

      const select = selects()[1];
      select.value = String(AttachmentKind.CoverLetter);
      select.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(host.items().map(i => i.kind)).toEqual([AttachmentKind.Cv, AttachmentKind.CoverLetter]);
      expect(selects()[1].value).toBe('1');
      expect(selects()[0].value).toBe('0');
    });

    it('labels every select with the visible "Rodzaj" label and the name of its file', () => {
      choose(makeFile('cv.pdf', 10), makeFile('list.docx', 10));

      expect(selects().map(labelledByText)).toEqual(['Rodzaj cv.pdf', 'Rodzaj list.docx']);
      const [first] = selects();
      expect(first.labels?.[0]?.textContent?.trim()).toBe('Rodzaj');
    });

    it('gives the selects of different files unique ids', () => {
      choose(makeFile('a.pdf', 10), makeFile('b.pdf', 10));

      const ids = selects().map(s => s.id);
      expect(new Set(ids).size).toBe(2);
    });
  });

  describe('validation', () => {
    it.each([
      ['cv.doc', 1000, 'Nieobsługiwany typ pliku – wybierz plik PDF, DOCX, PNG lub JPEG'],
      ['cv.pdf', 5 * MB + 1, 'Plik jest za duży – maksymalny rozmiar to 5 MB'],
      ['cv.docx', 0, 'Wybrany plik jest pusty']
    ])('rejects %s (%i B) with a Polish message naming the file', (name, size, message) => {
      choose(makeFile(name, size));

      expect(host.items()).toEqual([]);
      expect(root().querySelector('.error')?.textContent).toContain(`${name}: ${message}`);
      expect(root().querySelector('.hint')).toBeNull();
    });

    it('accepts the valid files of a mixed selection and lists the reasons for the others', () => {
      const ok = makeFile('ok.pdf', 10);

      choose(makeFile('zly.exe', 10), ok, makeFile('pusty.pdf', 0));

      expect(host.items().map(i => i.file)).toEqual([ok]);
      const lines = Array.from(root().querySelectorAll('.error-line')).map(e => e.textContent);
      expect(lines).toEqual([
        'zly.exe: Nieobsługiwany typ pliku – wybierz plik PDF, DOCX, PNG lub JPEG',
        'pusty.pdf: Wybrany plik jest pusty'
      ]);
    });

    it('rejects a file that would exceed 25 MB together with the existing attachments', () => {
      host.existing.set(existing(4, 5 * MB)); // 20 MB na serwerze
      choose(makeFile('a.pdf', 4 * MB));

      choose(makeFile('b.pdf', 2 * MB));

      expect(names()).toEqual(['a.pdf']);
      expect(root().querySelector('.error')?.textContent).toContain('b.pdf: Łączny rozmiar załączników nie może przekroczyć 25 MB');
    });

    it('accepts a file that fills the 25 MB limit exactly', () => {
      host.existing.set(existing(4, 5 * MB));

      choose(makeFile('ostatni.pdf', 5 * MB));

      expect(names()).toEqual(['ostatni.pdf']);
      expect(root().querySelector('.usage')?.textContent).toContain('Wykorzystano 25 MB z 25 MB');
    });

    it('allows at most 10 attachments including the existing ones and then disables the picker', () => {
      host.existing.set(existing(8));

      choose(makeFile('a.pdf', 10), makeFile('b.pdf', 10), makeFile('c.pdf', 10));

      expect(names()).toEqual(['a.pdf', 'b.pdf']);
      expect(root().querySelector('.error')?.textContent).toContain('c.pdf: Aplikacja może mieć maksymalnie 10 załączników');
      expect(fileInput().disabled).toBe(true);
      expect(root().querySelector('.choose-button')?.classList).toContain('disabled');
      expect(root().querySelector('.usage')?.textContent).toContain('plików: 10 z 10');
    });

    it('says that the limit is reached when the application already has 10 attachments', () => {
      host.existing.set(existing(10));

      expect(root().querySelector('.hint')?.textContent).toContain('Osiągnięto limit 10 załączników');
      expect(fileInput().disabled).toBe(true);
    });

    it('does not add the same file twice', () => {
      const file = makeFile('cv.pdf', 10);
      choose(file);

      choose(file);

      expect(names()).toEqual(['cv.pdf']);
      expect(root().querySelector('.error')?.textContent).toContain('cv.pdf: Ten plik jest już na liście');
    });

    it('keeps the previously selected files when a new choice is invalid', () => {
      const valid = makeFile('cv.pdf', 10);
      choose(valid);

      choose(makeFile('cv.exe', 10));

      expect(host.items().map(i => i.file)).toEqual([valid]);
      expect(root().querySelector('.error')).not.toBeNull();
    });

    it('clears the error after a valid file is chosen', () => {
      choose(makeFile('cv.txt', 10));
      choose(makeFile('cv.pdf', 10));

      expect(root().querySelector('.error')).toBeNull();
    });

    it('ignores a cancelled picker', () => {
      choose();

      expect(host.items()).toEqual([]);
      expect(root().querySelector('.error')).toBeNull();
    });

    it('shows an external (API) error in the same announced area', () => {
      host.apiError.set('Łączny rozmiar załączników nie może przekroczyć 25 MB');

      const error = root().querySelector('.error');
      expect(error?.textContent).toContain('Łączny rozmiar załączników nie może przekroczyć 25 MB');
      expect(error?.getAttribute('role')).toBe('alert');
    });

    it('prefers the validation error over the external error', () => {
      host.apiError.set('Błąd API');
      choose(makeFile('cv.txt', 10));

      expect(root().querySelector('.error')?.textContent).toContain('Nieobsługiwany typ pliku');
      expect(root().querySelector('.error')?.textContent).not.toContain('Błąd API');
    });
  });

  it('counts the existing and the selected files in the usage counter', () => {
    host.existing.set(existing(2, 3 * MB));

    choose(makeFile('a.pdf', MB / 2));

    expect(root().querySelector('.usage')?.textContent).toBe('Wykorzystano 6,5 MB z 25 MB · plików: 3 z 10');
  });

  describe('removing', () => {
    it('removes a file with its remove button', () => {
      choose(makeFile('a.pdf', 10), makeFile('b.pdf', 10));

      removeButtons()[0].click();
      fixture.detectChanges();

      expect(names()).toEqual(['b.pdf']);
      expect(host.items().length).toBe(1);
    });

    it('moves the focus to the next file after removing one', () => {
      choose(makeFile('a.pdf', 10), makeFile('b.pdf', 10), makeFile('c.pdf', 10));
      removeButtons()[1].focus();

      removeButtons()[1].click();
      fixture.detectChanges();

      expect(names()).toEqual(['a.pdf', 'c.pdf']);
      expect(document.activeElement).toBe(removeButtons()[1]);
      expect(document.activeElement?.getAttribute('aria-label')).toBe('Usuń z listy: c.pdf');
    });

    it('moves the focus to the previous file after removing the last one', () => {
      choose(makeFile('a.pdf', 10), makeFile('b.pdf', 10));

      removeButtons()[1].click();
      fixture.detectChanges();

      expect(document.activeElement?.getAttribute('aria-label')).toBe('Usuń z listy: a.pdf');
    });

    it('moves the focus to the file input after removing the only file', () => {
      choose(makeFile('a.pdf', 10));

      removeButtons()[0].click();
      fixture.detectChanges();

      expect(root().querySelector('.files')).toBeNull();
      expect(document.activeElement).toBe(fileInput());
    });

    it('moves the focus to the file input also when the list was full (10 of 10) before removing', () => {
      host.existing.set(existing(9));
      choose(makeFile('dziesiaty.pdf', 10));
      expect(fileInput().disabled).toBe(true);

      removeButtons()[0].click();
      fixture.detectChanges();

      expect(fileInput().disabled).toBe(false);
      expect(document.activeElement).toBe(fileInput());
    });

    it('clears a validation error after removing a file', () => {
      choose(makeFile('a.pdf', 10));
      choose(makeFile('zly.txt', 10));

      removeButtons()[0].click();

      expect(root().querySelector('.error')).toBeNull();
    });
  });

  describe('accessibility', () => {
    it('uses a real, keyboard-focusable file input labelled by the visible choose button', () => {
      const input = fileInput();
      const label = root().querySelector<HTMLLabelElement>('label.choose-button')!;

      expect(input.id).toMatch(/^attachments-picker-\d+$/);
      expect(label.htmlFor).toBe(input.id);
      expect(input.hidden).toBe(false);
      expect(input.tabIndex).toBe(0);
      expect(input.labels?.[0].textContent).toContain('Wybierz pliki');
      input.focus();
      expect(document.activeElement).toBe(input);
    });

    it('describes the restrictions and the usage counter through aria-describedby', () => {
      const ids = fileInput().getAttribute('aria-describedby')!.split(' ');

      expect(ids.length).toBe(2);
      expect(root().querySelector('#' + ids[0])?.textContent).toContain('PDF, DOCX, PNG lub JPEG – maks. 5 MB na plik');
      expect(root().querySelector('#' + ids[1])?.textContent).toContain('Wykorzystano 0 MB z 25 MB');
    });

    it('gives every instance unique ids', () => {
      const other = TestBed.createComponent(HostComponent);
      other.detectChanges();
      const otherInput = (other.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type="file"]')!;

      expect(otherInput.id).not.toBe(fileInput().id);
      expect(otherInput.getAttribute('aria-describedby')).not.toBe(fileInput().getAttribute('aria-describedby'));
    });

    it('keeps aria-describedby pointing at existing elements in the hint, error and progress states', () => {
      const described = () => fileInput().getAttribute('aria-describedby')!.split(' ')
        .map(id => root().querySelector<HTMLElement>('#' + id));

      expect(described()[0]?.querySelector('.hint')).not.toBeNull();

      choose(makeFile('cv.txt', 1));
      expect(described()[0]?.querySelector('[role="alert"]')).not.toBeNull();

      host.progress.set(20);
      expect(described()[0]?.querySelector('mat-progress-bar')).not.toBeNull();

      host.progress.set(null);
      expect(described().every(element => element !== null)).toBe(true);
    });

    it('renders hint, error and progress in the same reserved status area (no layout shift)', () => {
      const status = root().querySelector('.status');

      host.progress.set(10);
      expect(root().querySelector('.status')).toBe(status);
      host.progress.set(null);
      choose(makeFile('cv.txt', 1));
      expect(root().querySelector('.status')).toBe(status);
    });

    it('labels the remove buttons with the file names', () => {
      choose(makeFile('cv.pdf', 10), makeFile('list.docx', 10));

      expect(removeButtons().map(b => b.getAttribute('aria-label'))).toEqual(['Usuń z listy: cv.pdf', 'Usuń z listy: list.docx']);
    });

    it('presents the selected files as a labelled list', () => {
      choose(makeFile('cv.pdf', 10));

      const list = root().querySelector('ul.files')!;
      expect(list.getAttribute('aria-label')).toBe('Pliki do wysłania');
      expect(list.querySelectorAll(':scope > li').length).toBe(1);
    });

    it('exposes the upload progress as an accessible progressbar', () => {
      host.progress.set(40);

      const bar = root().querySelector('mat-progress-bar')!;
      expect(bar.getAttribute('role')).toBe('progressbar');
      expect(bar.getAttribute('aria-valuenow')).toBe('40');
      expect(bar.getAttribute('aria-valuetext')).toBe('Wysyłanie… 40%');
      expect(bar.getAttribute('aria-label')).toBe('Postęp wysyłania załączników');
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
      choose(makeFile('cv.pdf', 10), makeFile('foto.png', 10));
      host.apiError.set('Błąd');

      const icons = Array.from(root().querySelectorAll('mat-icon'));
      expect(icons.length).toBeGreaterThan(0);
      expect(icons.every(icon => icon.getAttribute('aria-hidden') === 'true')).toBe(true);
    });

    it('tells the user about the error with an icon and text, not colour alone', () => {
      choose(makeFile('cv.txt', 1));

      const error = root().querySelector('.error')!;
      expect(error.querySelector('mat-icon')?.textContent).toContain('error_outline');
      expect(error.querySelector('.error-line')?.textContent?.trim()).not.toBe('');
    });

    it('does not hide the file input from assistive technology', () => {
      const input = fileInput();

      expect(input.getAttribute('aria-hidden')).toBeNull();
      expect(input.getAttribute('tabindex')).toBeNull();
    });

    it('keeps the remove button reachable by keyboard', () => {
      expect(root().querySelectorAll('button').length).toBe(0);

      choose(makeFile('cv.pdf', 10));
      const remove = removeButtons()[0];
      remove.focus();

      expect(remove.disabled).toBe(false);
      expect(remove.tabIndex).toBe(0);
      expect(document.activeElement).toBe(remove);
    });
  });

  describe('announcements (one role="status")', () => {
    it('has exactly one status region, visually hidden and present from the start', () => {
      const regions = root().querySelectorAll('[role="status"]');

      expect(regions.length).toBe(1);
      expect(regions[0].classList).toContain('sr-only');
      expect(regions[0].textContent?.trim()).toBe('');
    });

    it('announces the added and the removed files', () => {
      choose(makeFile('a.pdf', 10), makeFile('b.docx', 10));
      expect(liveStatus().textContent).toBe('Dodano do listy: a.pdf, b.docx');

      removeButtons()[0].click();
      expect(liveStatus().textContent).toBe('Usunięto z listy: a.pdf');
    });

    it('announces the start of the upload once, not every percent', () => {
      const region = liveStatus();
      host.progress.set(0);
      expect(liveStatus().textContent).toBe('Wysyłanie załączników…');

      const textNode = region.firstChild;
      host.progress.set(35);
      root();
      host.progress.set(80);
      root();

      expect(liveStatus()).toBe(region);
      expect(region.textContent).toBe('Wysyłanie załączników…');
      expect(region.firstChild).toBe(textNode);
    });

    it('announces the processing phase after the whole request has been sent', () => {
      host.progress.set(100);

      expect(liveStatus().textContent).toBe('Przetwarzanie załączników…');
    });

    it('announces the final state given by the parent', () => {
      host.progress.set(50);
      host.progress.set(null);
      host.statusMessage.set('Przerwano wysyłanie załączników');

      expect(liveStatus().textContent).toBe('Przerwano wysyłanie załączników');
    });
  });

  describe('while uploading', () => {
    it('shows the progress and blocks changes', () => {
      choose(makeFile('cv.pdf', 10));
      host.progress.set(40);

      expect(root().querySelector('mat-progress-bar')).not.toBeNull();
      expect(statusText()).toContain('Wysyłanie… 40%');
      expect(removeButtons().length).toBe(0);
      expect(fileInput().disabled).toBe(true);
      expect(selects().every(s => s.disabled)).toBe(true);
    });

    it('shows the progress bar also at 0 %', () => {
      host.progress.set(0);

      expect(statusText()).toContain('Wysyłanie… 0%');
    });

    it('switches to "Przetwarzanie…" when the whole request has been sent', () => {
      host.progress.set(100);

      expect(statusText()).toContain('Przetwarzanie…');
      expect(root().querySelector('mat-progress-bar')?.getAttribute('aria-valuetext')).toBe('Przetwarzanie…');
    });

    it('shows no progress bar when nothing is uploading', () => {
      expect(root().querySelector('mat-progress-bar')).toBeNull();
    });
  });

  it('disables the file input and the selects when disabled', () => {
    choose(makeFile('cv.pdf', 10));
    host.disabled.set(true);

    expect(fileInput().disabled).toBe(true);
    expect(root().querySelector('.choose-button')?.classList).toContain('disabled');
    expect(selects()[0].disabled).toBe(true);
    expect(removeButtons()[0].disabled).toBe(true);
  });

  describe('file size and name edge cases', () => {
    it('accepts a file of exactly 5 MB', () => {
      const file = makeFile('cv.pdf', 5 * MB);

      choose(file);

      expect(host.items()[0].file).toBe(file);
      expect(root().querySelector('.error')).toBeNull();
      expect(root().querySelector('.file-size')?.textContent).toContain('5 MB');
    });

    it.each(['CV.PDF', 'Cv.Docx', 'cv.pDf', 'FOTO.JPEG', 'skan.Png'])('accepts the upper-case extension of %s', name => {
      choose(makeFile(name, 100));

      expect(host.items().length).toBe(1);
      expect(root().querySelector('.error')).toBeNull();
    });

    it.each(['cv', 'pdf', '.hidden', 'cv.', 'cv.pdf.exe', 'cv.docx.txt', 'obraz.gif', 'obraz.svg'])('rejects %s (no valid extension)', name => {
      choose(makeFile(name, 100));

      expect(host.items()).toEqual([]);
      expect(root().querySelector('.error')?.textContent).toContain('Nieobsługiwany typ pliku');
    });

    it('treats a cancelled picker (empty file list) as no change and keeps the previous files and error', () => {
      choose(makeFile('cv.pdf', 10));
      choose(makeFile('zly.txt', 10));

      choose();

      expect(names()).toEqual(['cv.pdf']);
      expect(root().querySelector('.error')?.textContent).toContain('Nieobsługiwany typ pliku');
    });

    it('survives a change event without a files list', () => {
      const input = fileInput();
      Object.defineProperty(input, 'files', { value: null, configurable: true });

      input.dispatchEvent(new Event('change'));

      expect(host.items()).toEqual([]);
      expect(root().querySelector('.error')).toBeNull();
    });

    it('uses an image icon for pictures and a document icon for the rest', () => {
      choose(makeFile('cv.pdf', 10), makeFile('foto.jpg', 10));

      const icons = Array.from(root().querySelectorAll('.file > mat-icon')).map(i => i.textContent?.trim());
      expect(icons).toEqual(['description', 'image']);
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
    ])('renders the file name %j as text only (list, remove button and kind label)', name => {
      choose(makeFile(name, 100));

      const view = root();
      const label = view.querySelector('.file-name')!;
      expect(label.textContent).toBe(name);
      expect(label.children.length).toBe(0);
      expect(view.querySelector('script, img, svg')).toBeNull();
      expect(removeButtons()[0].getAttribute('aria-label')).toBe(`Usuń z listy: ${name}`);
      expect(labelledByText(selects()[0])).toBe(`Rodzaj ${name.trim()}`);
      expect(liveStatus().textContent).toBe(`Dodano do listy: ${name}`);
      expect((window as unknown as { __pwned?: boolean }).__pwned).toBeUndefined();
    });

    it('renders a rejected file name with markup as text only in the error', () => {
      choose(makeFile('<img src=x onerror="window.__pwned = true">.exe', 10));

      expect(root().querySelector('.error-line')?.textContent).toContain('<img src=x onerror="window.__pwned = true">.exe');
      expect(root().querySelector('img')).toBeNull();
    });

    it('renders an API error containing markup as text only', () => {
      host.apiError.set('<img src=x onerror="window.__pwned = true"> błąd');

      const view = root();
      expect(view.querySelector('.error-line')?.textContent).toBe('<img src=x onerror="window.__pwned = true"> błąd');
      expect(view.querySelector('img')).toBeNull();
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
        // Angular 22 planuje kolejne sprawdzenie zmian na najbliższą klatkę (wyścig setTimeout/requestAnimationFrame
        // planisty aplikacji, nie komponentu) - klatka jest przewijana, dłuższe timery komponentu zostałyby w liczniku
        vi.advanceTimersToNextFrame();

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
      const picker = (other.nativeElement as HTMLElement).querySelector('app-attachments-picker')!;
      const component = other.debugElement.children[0].componentInstance as AttachmentsPickerComponent;
      component.itemsChange.subscribe(emitted);

      other.destroy();
      other.componentInstance.progress.set(30);
      other.componentInstance.apiError.set('Po zniszczeniu');

      expect(emitted).not.toHaveBeenCalled();
      expect(picker.textContent).not.toContain('Po zniszczeniu');
    });
  });
});
