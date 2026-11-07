import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { ConfirmService } from './confirm.service';
import { ConfirmDialogComponent } from '../components/shared/confirm-dialog.component';

describe('ConfirmService', () => {
  function setup(closeWith: unknown) {
    const open = vi.fn().mockReturnValue({ afterClosed: () => of(closeWith) });
    TestBed.configureTestingModule({ providers: [{ provide: MatDialog, useValue: { open } }] });
    return { service: TestBed.inject(ConfirmService), open };
  }

  it('opens the confirm dialog as a modal with the given data', () => {
    const { service, open } = setup(true);
    const data = { title: 'Usuń ofertę', message: 'Tej operacji nie da się cofnąć.', confirmLabel: 'Usuń', destructive: true };

    service.confirm(data).subscribe();

    expect(open).toHaveBeenCalledWith(ConfirmDialogComponent, expect.objectContaining({ data, ariaModal: true, restoreFocus: true }));
  });

  it('emits true only when the user confirms', () => {
    const results: boolean[] = [];
    setup(true).service.confirm({ title: 't', message: 'm', confirmLabel: 'OK' }).subscribe(r => results.push(r));
    TestBed.resetTestingModule();
    setup(undefined).service.confirm({ title: 't', message: 'm', confirmLabel: 'OK' }).subscribe(r => results.push(r));
    TestBed.resetTestingModule();
    setup(false).service.confirm({ title: 't', message: 'm', confirmLabel: 'OK' }).subscribe(r => results.push(r));

    expect(results).toEqual([true, false, false]);
  });
});
