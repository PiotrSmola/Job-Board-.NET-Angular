import { PolishPaginatorIntl } from './polish-paginator-intl';

describe('PolishPaginatorIntl', () => {
  const intl = new PolishPaginatorIntl();

  it('has Polish labels for the page size and the navigation buttons', () => {
    expect(intl.itemsPerPageLabel).toBe('Na stronie:');
    expect(intl.firstPageLabel).toBe('Pierwsza strona');
    expect(intl.previousPageLabel).toBe('Poprzednia strona');
    expect(intl.nextPageLabel).toBe('Następna strona');
    expect(intl.lastPageLabel).toBe('Ostatnia strona');
  });

  it.each([
    [0, 10, 42, '1–10 z 42'],
    [4, 10, 42, '41–42 z 42'],
    [0, 10, 0, '0 z 0'],
    [0, 0, 5, '0 z 5'],
    // strona poza zakresem (np. po usunięciu elementów) – jak domyślna implementacja Material
    [5, 10, 42, '51–60 z 42']
  ])('formats page %i (size %i) of %i items as "%s"', (page, size, length, expected) => {
    expect(intl.getRangeLabel(page, size, length)).toBe(expected);
  });
});
