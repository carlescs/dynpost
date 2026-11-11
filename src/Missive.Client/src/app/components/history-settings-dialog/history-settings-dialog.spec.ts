import { DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../services/bridge';
import { HistorySettingsDialog } from './history-settings-dialog';

describe('HistorySettingsDialog', () => {
  async function setup() {
    const history = { enabled: true, maxEntries: 20, maxBodyBytes: 1024 * 1024 };
    const send = vi.fn().mockResolvedValue({ theme: 'light', history });
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [HistorySettingsDialog],
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: DialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(HistorySettingsDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const input = (name: string) => root.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
    const type = (name: string, value: string) => {
      input(name).value = value;
      input(name).dispatchEvent(new Event('input'));
    };
    const submit = async () => {
      root.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { root, send, close, input, type, submit };
  }

  it('shows the settings and saves the changes in bytes', async () => {
    const { send, close, input, type, submit } = await setup();
    expect(input('maxEntries').value).toBe('20');
    expect(input('maxBodyKb').value).toBe('1024');

    type('maxEntries', '50');
    type('maxBodyKb', '256');
    await submit();

    expect(send).toHaveBeenLastCalledWith('settings.setHistory', {
      enabled: true,
      maxEntries: 50,
      maxBodyBytes: 256 * 1024,
    });
    expect(close).toHaveBeenCalled();
  });

  it('refuses limits out of range without saving', async () => {
    const { root, send, close, type, submit } = await setup();

    type('maxEntries', '0');
    await submit();

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('between 1 and 500');
    expect(send).not.toHaveBeenCalledWith('settings.setHistory', expect.anything());
    expect(close).not.toHaveBeenCalled();
  });
});
