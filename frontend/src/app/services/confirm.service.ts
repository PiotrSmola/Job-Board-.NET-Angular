import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, map } from 'rxjs';
import { ConfirmDialogComponent, ConfirmDialogData } from '../components/shared/confirm-dialog.component';

/** Potwierdzenia akcji przez MatDialog (dostępne, nie blokują wątku jak window.confirm). */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private dialog = inject(MatDialog);

  confirm(data: ConfirmDialogData): Observable<boolean> {
    return this.dialog
      .open<ConfirmDialogComponent, ConfirmDialogData, boolean>(ConfirmDialogComponent, {
        data,
        ariaModal: true,
        autoFocus: 'first-tabbable',
        restoreFocus: true,
        width: 'min(440px, 92vw)'
      })
      .afterClosed()
      .pipe(map(result => result === true));
  }
}
