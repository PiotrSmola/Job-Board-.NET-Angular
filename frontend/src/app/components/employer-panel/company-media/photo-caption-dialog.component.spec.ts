import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { PhotoCaptionDialogComponent } from './photo-caption-dialog.component';

describe('PhotoCaptionDialogComponent', () => {
  let close: ReturnType<typeof vi.fn>;

  function create(caption: string | null) {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [PhotoCaptionDialogComponent],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { caption } },
        { provide: MatDialogRef, useValue: { close } }
      ]
    });
    const fixture = TestBed.createComponent(PhotoCaptionDialogComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('prefills the caption and shows the 150 character limit', () => {
    const fixture = create('Biuro');
    const root = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.caption.value).toBe('Biuro');
    expect(root.querySelector('input')?.getAttribute('maxlength')).toBe('150');
    expect(root.textContent).toContain('5 / 150');
    expect(root.querySelector('h2')?.textContent).toBe('Opis zdjęcia');
  });

  it('closes with the trimmed caption (whitespace collapsed) on submit', () => {
    const fixture = create(null);
    fixture.componentInstance.caption.setValue('  Sala   spotkań ');

    (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));

    expect(close).toHaveBeenCalledWith('Sala spotkań');
  });

  it('closes with null for an empty caption', () => {
    const fixture = create('Biuro');
    fixture.componentInstance.caption.setValue('   ');

    fixture.componentInstance.save();

    expect(close).toHaveBeenCalledWith(null);
  });

  it('does not close with a caption over 150 characters', () => {
    const fixture = create(null);
    fixture.componentInstance.caption.setValue('x'.repeat(151));

    fixture.componentInstance.save();

    expect(close).not.toHaveBeenCalled();
  });
});
