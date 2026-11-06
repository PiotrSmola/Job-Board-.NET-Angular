/** Kategoria ofert (zarządzana przez admina); `activeJobsCount` – aktywne, niewygasłe oferty. */
export interface Category {
  id: number;
  name: string;
  slug: string;
  sortOrder: number;
  activeJobsCount: number;
}

/** Body POST/PUT /api/admin/categories. */
export interface SaveCategory {
  name: string;
  slug: string;
  sortOrder: number;
}

/** Podpowiedź umiejętności z GET /api/skills. */
export interface SkillSuggestion {
  name: string;
  jobsCount: number;
}

export interface FacetCategory {
  id: number;
  slug: string;
  name: string;
  count: number;
}

/** Licznik wartości enuma (poziom, tryb pracy) albo pojedynczego bitu typu umowy. */
export interface FacetValue {
  value: number;
  count: number;
}

export interface FacetSkill {
  name: string;
  count: number;
}

/** GET /api/jobs/facets – liczniki fasety X pomijają filtr X. */
export interface JobFacets {
  total: number;
  categories: FacetCategory[];
  seniority: FacetValue[];
  contractTypes: FacetValue[];
  workTypes: FacetValue[];
  skills: FacetSkill[];
}
