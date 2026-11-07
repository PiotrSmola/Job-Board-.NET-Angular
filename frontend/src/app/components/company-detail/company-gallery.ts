import { CompanyPhoto } from '../../models/company-media.model';

/** Tekst alternatywny zdjęcia: podpis albo „Zdjęcie firmy {nazwa} {n}” (n od 1). */
export function photoAlt(photo: Pick<CompanyPhoto, 'caption'>, companyName: string, index: number): string {
  const caption = photo.caption?.trim();
  return caption ? caption : `Zdjęcie firmy ${companyName} ${index + 1}`;
}

/** „zdjęcie / zdjęcia / zdjęć” po liczbie („+8 zdjęć”). */
export function photosNoun(count: number): string {
  if (count === 1) return 'zdjęcie';
  const last = count % 10;
  const lastTwo = count % 100;
  return last >= 2 && last <= 4 && !(lastTwo >= 12 && lastTwo <= 14) ? 'zdjęcia' : 'zdjęć';
}
