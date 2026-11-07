import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ProfileDocumentsPickerComponent } from './profile-documents-picker.component';
import { AccountService } from '../../services/account.service';
import { ProfileDocument } from '../../models/account.model';
import { AttachmentKind, AttachmentUploadItem } from '../../models/job-application.model';

vi.setConfig({ testTimeout: 20000 });

const MB = 1024 * 1024;

function doc(id: number, size = 100 * 1024, kind = AttachmentKind.Cv): ProfileDocument {
  return { id, kind, fileName: `dok-${id}.pdf`, size, uploadedAt: '2026-01-01T10:00:00Z' };
}

function fileOfSize(name: string, size: number): File {
  const file = new File(['x'], name);
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('ProfileDocumentsPickerComponent', () => {
  let fixture: ComponentFixture<ProfileDocumentsPickerComponent>;
  let getDocuments: ReturnType<typeof vi.fn>;
  let emitted: ProfileDocument[][];

  function create(documents: ProfileDocument[] | Subject<ProfileDocument[]> | 'error' = [doc(1), doc(2, 200 * 1024, AttachmentKind.CoverLetter)]): void {
    getDocuments.mockReturnValue(documents === 'error'
      ? throwError(() => new HttpErrorResponse({ status: 500 }))
      : Array.isArray(documents) ? of(documents) : documents);
    fixture = TestBed.createComponent(ProfileDocumentsPickerComponent);
    emitted = [];
    fixture.componentInstance.selectionChange.subscribe(selection => emitted.push(selection));
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function checkboxes(): HTMLInputElement[] {
    return Array.from(root().querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));
  }

  beforeEach(() => {
    getDocuments = vi.fn();
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [ProfileDocumentsPickerComponent],
      providers: [provideRouter([]), { provide: AccountService, useValue: { getDocuments } }]
    });
  });

  afterEach(() => vi.restoreAllMocks());

  it('groups the documents in a labelled fieldset with a checkbox per document', () => {
    create();

    expect(root().querySelector('fieldset legend')?.textContent).toContain('Dołącz z profilu');
    expect(checkboxes().length).toBe(2);
    const labels = Array.from(root().querySelectorAll('mat-checkbox label')).map(l => l.textContent?.replace(/\s+/g, ' ').trim());
    expect(labels).toEqual(['dok-1.pdf (CV, 100 KB)', 'dok-2.pdf (List motywacyjny, 200 KB)']);
    expect(root().querySelector('fieldset')!.getAttribute('aria-describedby')).toBe(root().querySelector('.hint[id]')!.id);
  });

  it('emits the selected documents', () => {
    create();

    checkboxes()[1].click();
    fixture.detectChanges();
    checkboxes()[0].click();
    fixture.detectChanges();

    expect(emitted.at(-1)?.map(d => d.id)).toEqual([1, 2]);

    checkboxes()[1].click();
    fixture.detectChanges();
    expect(emitted.at(-1)?.map(d => d.id)).toEqual([1]);
  });

  it('blocks documents that would exceed 25 MB together with the files picked for upload', () => {
    create([doc(1, 4 * MB), doc(2, 4 * MB)]);
    const pending: AttachmentUploadItem[] = Array.from({ length: 5 }, (_, i) => ({
      file: fileOfSize(`f${i}.pdf`, 4 * MB), kind: AttachmentKind.Other
    }));
    fixture.componentRef.setInput('pendingItems', pending);
    fixture.detectChanges();

    checkboxes()[0].click();
    fixture.detectChanges();

    expect(checkboxes()[0].checked).toBe(true);
    expect(checkboxes()[1].disabled).toBe(true);
    expect(root().textContent).toContain('przekroczyłaby łączny limit 25 MB');
  });

  it('blocks further documents once 10 attachments are reached in total', () => {
    create([doc(1), doc(2)]);
    fixture.componentRef.setInput('pendingItems', Array.from({ length: 9 }, (_, i) => ({
      file: fileOfSize(`f${i}.pdf`, 1000), kind: AttachmentKind.Other
    })));
    fixture.detectChanges();

    checkboxes()[0].click();
    fixture.detectChanges();

    expect(checkboxes()[1].disabled).toBe(true);
    expect(checkboxes()[0].disabled).toBe(false);
    expect(root().textContent).toContain('Osiągnięto limit 10 załączników');
  });

  it('reserves space while loading and explains an empty profile with a link to the account', () => {
    const documents = new Subject<ProfileDocument[]>();
    create(documents);
    expect(root().querySelector('.body')!.getAttribute('aria-busy')).toBe('true');
    expect(root().textContent).toContain('Wczytywanie dokumentów z profilu');

    documents.next([]);
    fixture.detectChanges();

    const link = root().querySelector<HTMLAnchorElement>('a[href]');
    expect(link?.getAttribute('href')).toBe('/account?tab=documents');
    // nowa karta – wypełniana aplikacja na stronie oferty zostaje
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener');
    expect(link?.textContent).toContain('(otwiera się w nowej karcie)');
    expect(root().textContent).toContain('Nie masz jeszcze dokumentów w profilu');
  });

  it('announces a loading error', () => {
    create('error');

    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Nie udało się wczytać dokumentów z profilu');
  });

  it('cancels the request when destroyed', () => {
    const documents = new Subject<ProfileDocument[]>();
    create(documents);

    fixture.destroy();

    expect(documents.observed).toBe(false);
  });
});
