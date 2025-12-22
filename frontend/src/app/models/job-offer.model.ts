export enum WorkType {
  Remote = 0,
  Hybrid = 1,
  Onsite = 2
}

export interface JobOffer {
  id: number;
  title: string;
  company: string;
  description: string;
  salaryMin: number | null;
  salaryMax: number | null;
  location: string;
  workType: WorkType;
  createdAt: Date;
}

export interface CreateJobOffer {
  title: string;
  company: string;
  description: string;
  salaryMin: number | null;
  salaryMax: number | null;
  location: string;
  workType: WorkType;
}
