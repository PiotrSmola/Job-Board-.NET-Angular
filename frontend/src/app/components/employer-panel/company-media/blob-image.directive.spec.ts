import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Subject, throwError } from 'rxjs';
import { BlobImageDirective } from './blob-image.directive';
import { EmployerMediaService } from '../../../services/employer-media.service';

@Component({
  imports: [BlobImageDirective],
  template: `@if (show()) { <img [appBlobSrc]="url()" alt=""> }`
})
class HostComponent {
  readonly url = signal<string | null>('/api/a');
  readonly show = signal(true);
}

describe('BlobImageDirective', () => {
  let responses: Record<string, Subject<Blob>>;
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;
  let originalCreate: typeof URL.createObjectURL;
  let originalRevoke: typeof URL.revokeObjectURL;
  let counter: number;

  beforeEach(() => {
    responses = {};
    counter = 0;
    originalCreate = URL.createObjectURL;
    originalRevoke = URL.revokeObjectURL;
    createObjectURL = vi.fn(() => `blob:test/${++counter}`);
    revokeObjectURL = vi.fn();
    URL.createObjectURL = createObjectURL as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = revokeObjectURL as unknown as typeof URL.revokeObjectURL;
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{
        provide: EmployerMediaService,
        useValue: { loadImage: (url: string) => (responses[url] = new Subject<Blob>()) }
      }]
    });
  });

  afterEach(() => {
    URL.createObjectURL = originalCreate;
    URL.revokeObjectURL = originalRevoke;
    vi.restoreAllMocks();
  });

  function img(fixture: { nativeElement: HTMLElement }): HTMLImageElement | null {
    return fixture.nativeElement.querySelector('img');
  }

  it('loads the image with HttpClient and sets a blob: src', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    expect(img(fixture)!.hasAttribute('src')).toBe(false);

    responses['/api/a'].next(new Blob(['x']));
    fixture.detectChanges();

    expect(img(fixture)!.getAttribute('src')).toBe('blob:test/1');
  });

  it('revokes the previous object URL when the address changes and cancels a pending load', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    responses['/api/a'].next(new Blob(['x']));

    fixture.componentInstance.url.set('/api/b');
    fixture.detectChanges();
    const pendingB = responses['/api/b'];
    fixture.componentInstance.url.set('/api/c');
    fixture.detectChanges();
    responses['/api/c'].next(new Blob(['y']));
    fixture.detectChanges();

    expect(pendingB.observed).toBe(false);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:test/1');
    expect(img(fixture)!.getAttribute('src')).toBe('blob:test/2');
  });

  it('revokes the object URL when destroyed', () => {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    responses['/api/a'].next(new Blob(['x']));

    fixture.componentInstance.show.set(false);
    fixture.detectChanges();

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:test/1');
  });

  it('leaves the image without src when loading fails', () => {
    TestBed.overrideProvider(EmployerMediaService, { useValue: { loadImage: () => throwError(() => new Error('403')) } });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    expect(img(fixture)!.hasAttribute('src')).toBe(false);
    expect(createObjectURL).not.toHaveBeenCalled();
  });
});
