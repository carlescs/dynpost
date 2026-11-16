import { DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../services/bridge';
import { SecretSettingsDialog } from './secret-settings-dialog';

describe('SecretSettingsDialog', () => {
  async function render(keychainAvailable: boolean) {
    const send = vi.fn(async (type: string, payload?: unknown) =>
      type === 'secrets.getSettings'
        ? { storage: 'file', useDotEnv: false, keychainAvailable, keychainName: 'Test keychain' }
        : { ...(payload as object), keychainAvailable, keychainName: 'Test keychain' },
    );
    const close = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { send } },
        { provide: DialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(SecretSettingsDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, send, close };
  }

  it('saves the chosen store and the .env option', async () => {
    const { fixture, element, send, close } = await render(true);

    element.querySelectorAll<HTMLInputElement>('input[name=storage]')[1].click();
    element.querySelector<HTMLInputElement>('input[name=useDotEnv]')!.click();
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(send).toHaveBeenCalledWith('secrets.setSettings', {
      storage: 'keychain',
      useDotEnv: true,
    });
    expect(close).toHaveBeenCalled();
  });

  it('disables the keychain when it is not available', async () => {
    const { element } = await render(false);

    expect(element.querySelectorAll<HTMLInputElement>('input[name=storage]')[1].disabled).toBe(
      true,
    );
    expect(element.textContent).toContain('Not available on this computer');
  });
});
