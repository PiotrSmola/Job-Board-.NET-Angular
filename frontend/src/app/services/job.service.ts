import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { JobOffer, CreateJobOffer } from '../models/job-offer.model';
import { PaginatedResult, PaginationHeader } from '../models/pagination.model';
import { JobParams } from '../models/job-params.model';

@Injectable({
  providedIn: 'root'
})
export class JobService {
  private http = inject(HttpClient);
  private readonly API_URL = 'http://localhost:5285/api/jobs';

  getJobs(jobParams: JobParams): Observable<PaginatedResult<JobOffer>> {
    let params = new HttpParams();

    params = params.append('pageNumber', jobParams.pageNumber.toString());
    params = params.append('pageSize', jobParams.pageSize.toString());

    if (jobParams.searchTerm) {
      params = params.append('searchTerm', jobParams.searchTerm);
    }

    if (jobParams.workType !== null) {
      params = params.append('workType', jobParams.workType.toString());
    }

    if (jobParams.minSalary) {
      params = params.append('minSalary', jobParams.minSalary.toString());
    }

    if (jobParams.maxSalary) {
      params = params.append('maxSalary', jobParams.maxSalary.toString());
    }

    if (jobParams.location) {
      params = params.append('location', jobParams.location);
    }

    params = params.append('orderBy', jobParams.orderBy);

    return this.http.get<JobOffer[]>(this.API_URL, {
      params,
      observe: 'response'
    }).pipe(
      map((response: HttpResponse<JobOffer[]>) => {
        const paginatedResult: PaginatedResult<JobOffer> = {
          result: response.body || [],
          pagination: {
            CurrentPage: 1,
            ItemsPerPage: 10,
            TotalItems: 0,
            TotalPages: 1
          }
        };

        const paginationHeader = response.headers.get('Pagination');
        if (paginationHeader) {
          paginatedResult.pagination = JSON.parse(paginationHeader);
        }

        return paginatedResult;
      })
    );
  }

  getJob(id: number): Observable<JobOffer> {
    return this.http.get<JobOffer>(`${this.API_URL}/${id}`);
  }

  createJob(jobOffer: CreateJobOffer): Observable<JobOffer> {
    return this.http.post<JobOffer>(this.API_URL, jobOffer);
  }

  updateJob(id: number, jobOffer: CreateJobOffer): Observable<JobOffer> {
    return this.http.put<JobOffer>(`${this.API_URL}/${id}`, jobOffer);
  }

  deleteJob(id: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/${id}`);
  }

  getAllJobs(): Observable<JobOffer[]> {
    const params = new HttpParams()
      .append('pageNumber', '1')
      .append('pageSize', '1000');

    return this.http.get<JobOffer[]>(this.API_URL, { params });
  }
}
