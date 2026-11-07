import { DestroyRef, Directive, ElementRef, Renderer2, inject, input } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap } from 'rxjs';
import { EmployerMediaService } from '../../../services/employer-media.service';

/**
 * `<img [appBlobSrc]="url">` – pobiera obraz przez HttpClient (z JWT) i podstawia adres `blob:`.
 * Obrazy firmy niezatwierdzonej API wydaje tylko członkom i adminom, a zwykłe `src` nie wysyła tokenu.
 * Poprzedni adres jest zwalniany (`revokeObjectURL`) przy zmianie i zniszczeniu; zmiana adresu anuluje pobieranie.
 * Bez obrazu atrybut `src` jest usuwany (wiązanie null dałoby żądanie o „null”).
 */
@Directive({
  selector: 'img[appBlobSrc]',
  standalone: true
})
export class BlobImageDirective {
  private media = inject(EmployerMediaService);
  private host = inject<ElementRef<HTMLImageElement>>(ElementRef);
  private renderer = inject(Renderer2);
  private objectUrl: string | null = null;

  readonly appBlobSrc = input<string | null>(null);

  constructor() {
    toObservable(this.appBlobSrc).pipe(
      switchMap(url => url
        ? this.media.loadImage(url).pipe(
          map(blob => URL.createObjectURL(blob)),
          catchError(error => {
            console.error('Error loading image:', error);
            return of(null);
          }))
        : of(null)),
      takeUntilDestroyed()
    ).subscribe(url => this.replace(url));

    inject(DestroyRef).onDestroy(() => this.replace(null));
  }

  private replace(url: string | null): void {
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = url;
    if (url) {
      this.renderer.setAttribute(this.host.nativeElement, 'src', url);
    } else {
      this.renderer.removeAttribute(this.host.nativeElement, 'src');
    }
  }
}
