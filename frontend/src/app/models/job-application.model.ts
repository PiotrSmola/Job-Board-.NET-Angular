import { JobOffer } from './job-offer.model';

export interface JobApplication {
  id: number;
  jobOfferId: number;
  userId: string;
  note: string | null;
  appliedAt: Date;
  userEmail: string;
  jobTitle: string;
  jobOffer?: JobOffer;
}

export interface CreateJobApplication {
  jobOfferId: number;
  note: string | null;
}
