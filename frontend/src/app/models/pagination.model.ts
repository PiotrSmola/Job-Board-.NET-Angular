export interface PaginationHeader {
  CurrentPage: number;
  ItemsPerPage: number;
  TotalItems: number;
  TotalPages: number;
}

export interface PaginatedResult<T> {
  result: T[];
  pagination: PaginationHeader;
}
