import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { AttachmentListComponent } from './attachment-list.component';
import { JobApplicationService } from '../../services/job-application.service';
import { ConfirmService } from '../../services/confirm.service';
import { ApplicationAttachment, AttachmentKind } from '../../models/job-application.model';

const CV: ApplicationAttachment = { id: 1, kind: AttachmentKind.Cv, fileName: 'jan-cv.pdf', size: 350 * 1024, uploadedAt: '2026-01-02T10:00:00Z' };
const LETTER: ApplicationAttachment = { id: 2, kind: AttachmentKind.CoverLetter, fileName: 'list.docx', size: 2048, uploadedAt: '2026-01-03T10:00:00Z' };
const PHOTO: ApplicationAttachment = { id: 3, kind: AttachmentKind.Portfolio, fileName: 'projekt.png', size: 1024 * 1024, uploadedAt: '2026-01-04T10:00:00Z' };

@Component({
  standalone: true,
  imports: [AttachmentListComponent],
  template: `
    <app-attachment-list
      [applicationId]="7"
      [attachments]="attachments()"
      [allowDelete]="allowDelete()"
      [showDate]="showDate()"
      [emptyText]="emptyText()"
      (deleted)="onDeleted($event)" />
  `
})
class HostComponent {
  readonly attachments = signal<ApplicationAttachment[]>([CV, LETTER, PHOTO]);
  readonly allowDelete = signal(false);
  readonly showDate = signal(false);
  readonly emptyText = signal('Brak');
  readonly deleted: ApplicationAttachment[] = [];

  onDeleted(attachment: ApplicationAttachment): void {
    this.deleted.push(attachment);
    this.attachments.update(list => list.filter(a => a.id !== attachment.id));
  }
}

// Pierwszy test pliku kompiluje szablon komponentu Material – przy zimnym starcie i obciążeniu bywa wolniejszy niż domyślne 5 s
vi.setConfig({ testTimeout: 20000 });

describe('AttachmentListComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let downloadAttachment: ReturnType<typeof vi.fn>;
  let deleteAttachment: ReturnType<typeof vi.fn>;
  let confirmSpy: ReturnType<typeof vi.fn>;

  function create(): void {
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function downloadButtons(): HTMLButtonElement[] {
    return Array.from(root().querySelectorAll<HTMLButtonElement>('.download-button'));
  }

  function deleteButtons(): HTMLButtonElement[] {
    return Array.from(root().querySelectorAll<HTMLButtonElement>('.delete-button'));
  }

  function list(): AttachmentListComponent {
    return fixture.debugElement.children[0].componentInstance as AttachmentListComponent;
  }

  beforeEach(async () => {
    downloadAttachment = vi.fn().mockReturnValue(of(undefined));
    deleteAttachment = vi.fn().mockReturnValue(of(undefined));
    confirmSpy = vi.fn().mockReturnValue(of(true));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        { provide: JobApplicationService, useValue: { downloadAttachment, deleteAttachment } },
        { provide: ConfirmService, useValue: { confirm: confirmSpy } }
      ]
    }).compileComponents();
    create();
  });

  afterEach(() => vi.restoreAllMocks());

  it('lists the attachments with their names, kinds and sizes', () => {
    const items = Array.from(root().querySelectorAll('li.attachment'));

    expect(items.map(li => li.querySelector('.file-name')?.textContent)).toEqual(['jan-cv.pdf', 'list.docx', 'projekt.png']);
    expect(items.map(li => li.querySelector('.meta')?.textContent)).toEqual(['CV · 350 KB', 'List motywacyjny · 2 KB', 'Portfolio · 1 MB']);
  });

  it('adds the upload date when asked to', () => {
    host.showDate.set(true);

    expect(root().querySelector('.meta')?.textContent).toBe('CV · 350 KB · 02.01.2026');
  });

  it('shows the empty text when there are no attachments', () => {
    host.attachments.set([]);
    host.emptyText.set('Kandydat nie dołączył załączników');

    expect(root().querySelector('ul')).toBeNull();
    expect(root().querySelector('.empty')?.textContent).toBe('Kandydat nie dołączył załączników');
  });

  it('labels the download buttons with the kind and the file name', () => {
    expect(downloadButtons().map(b => b.getAttribute('aria-label'))).toEqual([
      'Pobierz CV: jan-cv.pdf', 'Pobierz List motywacyjny: list.docx', 'Pobierz Portfolio: projekt.png'
    ]);
  });

  it('offers no delete buttons unless deleting is allowed', () => {
    expect(deleteButtons().length).toBe(0);

    host.allowDelete.set(true);

    expect(deleteButtons().map(b => b.getAttribute('aria-label'))).toEqual([
      'Usuń CV: jan-cv.pdf', 'Usuń List motywacyjny: list.docx', 'Usuń Portfolio: projekt.png'
    ]);
  });

  describe('downloading', () => {
    it('downloads the clicked attachment of the application', () => {
      downloadButtons()[1].click();

      expect(downloadAttachment).toHaveBeenCalledWith(7, LETTER);
      expect(list().downloadingIds().size).toBe(0);
    });

    it('marks the button with aria-disabled (not disabled) while downloading, so the focus stays on it', () => {
      const pending = new Subject<void>();
      downloadAttachment.mockReturnValue(pending);
      const button = downloadButtons()[0];
      button.focus();

      button.click();
      root();

      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.disabled).toBe(false);
      expect(button.hasAttribute('disabled')).toBe(false);
      expect(document.activeElement).toBe(button);
      expect(button.querySelector('mat-spinner')).not.toBeNull();
      expect(downloadButtons()[1].getAttribute('aria-disabled')).toBeNull();

      pending.next();
      pending.complete();
      root();
      expect(button.getAttribute('aria-disabled')).toBeNull();
      expect(document.activeElement).toBe(button);
    });

    it('ignores further clicks on a file that is still downloading', () => {
      downloadAttachment.mockReturnValue(new Subject<void>());

      downloadButtons()[0].click();
      root();
      downloadButtons()[0].click();

      expect(downloadAttachment).toHaveBeenCalledTimes(1);
    });

    it('shows the translated API message in an alert when the download fails', () => {
      downloadAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Attachment not found' } })));

      downloadButtons()[0].click();

      const error = root().querySelector('.error');
      expect(error?.getAttribute('role')).toBe('alert');
      expect(error?.textContent).toBe('Nie znaleziono załącznika');
      expect(downloadButtons()[0].getAttribute('aria-disabled')).toBeNull();
    });

    it('shows a generic message for server errors', () => {
      downloadAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'boom' } })));

      downloadButtons()[0].click();

      expect(root().querySelector('.error')?.textContent).toBe('Nie udało się pobrać załącznika');
    });

    it('translates the rate limit response', () => {
      downloadAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429 })));

      downloadButtons()[0].click();

      expect(root().querySelector('.error')?.textContent).toBe('Zbyt wiele żądań – spróbuj ponownie za kilka minut');
    });

    it.each([401, 403])('does not duplicate the interceptor notice for %i', status => {
      downloadAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));

      downloadButtons()[0].click();

      expect(root().querySelector('.error')).toBeNull();
    });

    it('cancels a pending download when the list is destroyed', () => {
      const pending = new Subject<void>();
      downloadAttachment.mockReturnValue(pending);

      downloadButtons()[0].click();
      expect(pending.observed).toBe(true);
      fixture.destroy();

      expect(pending.observed).toBe(false);
    });
  });

  describe('deleting', () => {
    beforeEach(() => host.allowDelete.set(true));

    it('deletes after confirmation and tells the parent', () => {
      deleteButtons()[1].click();

      expect(confirmSpy).toHaveBeenCalledWith({
        title: 'Usunąć załącznik?',
        message: '„list.docx” (List motywacyjny) zostanie usunięty. Tej operacji nie da się cofnąć.',
        confirmLabel: 'Usuń',
        destructive: true
      });
      expect(deleteAttachment).toHaveBeenCalledWith(7, 2);
      expect(host.deleted).toEqual([LETTER]);
      expect(Array.from(root().querySelectorAll('.file-name')).map(e => e.textContent)).toEqual(['jan-cv.pdf', 'projekt.png']);
    });

    it('does not delete when the confirmation is declined', () => {
      confirmSpy.mockReturnValue(of(false));

      deleteButtons()[0].click();

      expect(deleteAttachment).not.toHaveBeenCalled();
      expect(host.deleted).toEqual([]);
    });

    it('marks the button with aria-disabled while deleting and ignores other deletions meanwhile', () => {
      const pending = new Subject<void>();
      deleteAttachment.mockReturnValue(pending);
      const button = deleteButtons()[0];

      button.click();
      root();
      deleteButtons()[1].click();

      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.hasAttribute('disabled')).toBe(false);
      expect(deleteAttachment).toHaveBeenCalledTimes(1);
    });

    it('keeps the attachment and shows the translated message when deleting fails', () => {
      deleteAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Application not found' } })));

      deleteButtons()[0].click();

      expect(host.deleted).toEqual([]);
      expect(root().querySelector('[role="alert"]')?.textContent).toBe('Aplikacja nie została znaleziona');
      expect(deleteButtons()[0].getAttribute('aria-disabled')).toBeNull();
    });

    it('treats an attachment that is already gone as deleted', () => {
      deleteAttachment.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Attachment not found' } })));

      deleteButtons()[0].click();

      expect(host.deleted).toEqual([CV]);
      expect(root().querySelector('[role="alert"]')).toBeNull();
    });

    it('moves the focus to the next attachment after deleting one', () => {
      deleteButtons()[1].focus();

      deleteButtons()[1].click();
      root();

      expect(document.activeElement?.getAttribute('aria-label')).toBe('Usuń Portfolio: projekt.png');
    });

    it('moves the focus to the previous attachment after deleting the last one', () => {
      deleteButtons()[2].click();
      root();

      expect(document.activeElement?.getAttribute('aria-label')).toBe('Usuń List motywacyjny: list.docx');
    });

    it('moves the focus to the empty-list text after deleting the only attachment', () => {
      host.attachments.set([CV]);
      root();

      deleteButtons()[0].click();
      root();

      const empty = root().querySelector<HTMLElement>('.empty')!;
      expect(empty.textContent).toBe('Brak');
      expect(empty.tabIndex).toBe(-1);
      expect(document.activeElement).toBe(empty);
    });

    it('does not steal the focus later when the parent keeps the list unchanged', () => {
      host.attachments.set([CV]);
      root();
      host.onDeleted = () => undefined;

      deleteButtons()[0].click();
      root();
      const outside = document.createElement('button');
      document.body.appendChild(outside);
      outside.focus();
      host.attachments.set([LETTER]);
      root();

      expect(document.activeElement).toBe(outside);
      outside.remove();
    });

    it('cancels a pending deletion when the list is destroyed', () => {
      const pending = new Subject<void>();
      deleteAttachment.mockReturnValue(pending);

      deleteButtons()[0].click();
      fixture.destroy();

      expect(pending.observed).toBe(false);
      expect(host.deleted).toEqual([]);
    });
  });

  describe('security', () => {
    it.each([
      '<script>window.__pwned = true</script>.pdf',
      '<img src=x onerror="window.__pwned = true">.png',
      '"><svg onload=alert(1)>.docx'
    ])('renders the file name %j as text only', name => {
      host.allowDelete.set(true);
      host.attachments.set([{ ...CV, fileName: name }]);

      const view = root();
      expect(view.querySelector('.file-name')?.textContent).toBe(name);
      expect(view.querySelector('script, img, svg')).toBeNull();
      expect(downloadButtons()[0].getAttribute('aria-label')).toBe(`Pobierz CV: ${name}`);
      expect(deleteButtons()[0].getAttribute('aria-label')).toBe(`Usuń CV: ${name}`);
      expect((window as unknown as { __pwned?: boolean }).__pwned).toBeUndefined();
    });
  });

  it('marks decorative icons as hidden from assistive technology', () => {
    host.allowDelete.set(true);

    const icons = Array.from(root().querySelectorAll('mat-icon'));
    expect(icons.length).toBe(6);
    expect(icons.every(icon => icon.getAttribute('aria-hidden') === 'true')).toBe(true);
  });
});
