import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { PageTitleService } from './page-title.service';

describe('PageTitleService', () => {
  let service: PageTitleService;
  let title: Title;
  let announce: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    announce = vi.fn().mockResolvedValue(undefined);
    TestBed.configureTestingModule({ providers: [{ provide: LiveAnnouncer, useValue: { announce } }] });
    service = TestBed.inject(PageTitleService);
    title = TestBed.inject(Title);
    title.setTitle('Oferta pracy – Job Board');
  });

  it('sets the data-dependent title and announces it politely', () => {
    service.set('Senior .NET Developer');

    expect(title.getTitle()).toBe('Senior .NET Developer – Job Board');
    expect(announce).toHaveBeenCalledExactlyOnceWith('Senior .NET Developer – Job Board', 'polite');
  });

  it('does not announce the same title twice (e.g. reloading the same offer)', () => {
    service.set('Senior .NET Developer');
    service.set('Senior .NET Developer');

    expect(announce).toHaveBeenCalledTimes(1);
  });

  it('announces a given title', () => {
    service.announce('Firmy – Job Board');

    expect(announce).toHaveBeenCalledWith('Firmy – Job Board', 'polite');
  });

  it('ignores a rejected announcement (e.g. the announcer was destroyed)', async () => {
    announce.mockRejectedValue(new Error('destroyed'));

    expect(() => service.set('Firmy')).not.toThrow();
    await Promise.resolve();
  });
});
