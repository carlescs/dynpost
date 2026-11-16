import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../../../services/bridge';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { ClientCertificates } from './client-certificates';

describe('ClientCertificates', () => {
  async function render(send: ReturnType<typeof vi.fn>) {
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { send } },
        { provide: WorkspaceStore, useValue: { error: { set: vi.fn() } } },
      ],
    });
    const fixture = TestBed.createComponent(ClientCertificates);
    fixture.componentRef.setInput('environmentId', 'dev.yaml');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('lists the certificates, showing a stored passphrase without its value', async () => {
    const send = vi
      .fn()
      .mockResolvedValue([
        { host: 'api.test', certificate: 'certs/a.pfx', key: null, hasPassphrase: true },
      ]);
    const { element } = await render(send);

    expect(send).toHaveBeenCalledWith('certificate.list', { environment: 'dev.yaml' });
    expect(element.querySelector<HTMLInputElement>('#cert-host-0')!.value).toBe('api.test');
    const passphrase = element.querySelector<HTMLInputElement>('#cert-pass-0')!;
    expect(passphrase.value).toBe('');
    expect(passphrase.placeholder).toContain('Stored');
  });

  it('saves the rows, keeping a stored passphrase that was not retyped', async () => {
    const send = vi.fn(async (type: string, payload?: any) =>
      type === 'certificate.list'
        ? [{ host: 'api.test', certificate: 'certs/a.pfx', hasPassphrase: true }]
        : payload.certificates.map((c: any) => ({ ...c, hasPassphrase: true })),
    );
    const { element, fixture } = await render(send);

    const host = element.querySelector<HTMLInputElement>('#cert-host-0')!;
    host.value = '*.api.test';
    host.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    [...element.querySelectorAll('button')].find((b) => b.textContent?.includes('Save'))!.click();
    await fixture.whenStable();

    expect(send).toHaveBeenCalledWith('certificate.save', {
      environment: 'dev.yaml',
      certificates: [
        { host: '*.api.test', certificate: 'certs/a.pfx', key: null, passphrase: null },
      ],
    });
  });
});
