import { JobOffer } from './job-offer.model';

export interface JobApplication {
  id: number;
  jobOfferId: number;
  userId: string;
  userEmail: string;
  userFullName: string;
  jobTitle: string;
  company: string;
  note: string | null;
  appliedAt: Date;
  jobOffer?: JobOffer;
}

export interface CreateJobApplication {
  jobOfferId: number;
  note: string | null;
}
