import { TestBed } from '@angular/core/testing';
import type { Mock } from 'vitest';
import { CookieDto } from '../../../../models/messages';
import { EnvironmentStore } from '../../../../services/environment-store';
import { CookieJar } from './cookie-jar';
import { Dialogs } from '../../../../services/dialogs';

const session: CookieDto = {
  name: 'session',
  value: 'abc',
  domain: 'api.test',
  path: '/',
  expires: null,
  secure: false,
  httpOnly: true,
};
const theme: CookieDto = { ...session, name: 'theme', value: 'dark' };

describe('CookieJar', () => {
  let dialogs: { confirm: Mock };
  beforeEach(() => (dialogs = { confirm: vi.fn().mockResolvedValue(true) }));
  let store: { cookies: any; deleteCookie: any; clearCookies: any };

  async function render() {
    TestBed.configureTestingModule({
      providers: [
        { provide: EnvironmentStore, useValue: store },
        { provide: Dialogs, useValue: dialogs },
      ],
    });
    const fixture = TestBed.createComponent(CookieJar);
    fixture.componentRef.setInput('environmentId', 'dev.yaml');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    store = {
      cookies: vi.fn().mockResolvedValue([session, theme]),
      deleteCookie: vi.fn().mockResolvedValue([theme]),
      clearCookies: vi.fn().mockResolvedValue(true),
    };
  });

  it('lists the environment’s cookies', async () => {
    const fixture = await render();
    expect(store.cookies).toHaveBeenCalledWith('dev.yaml');
    expect(fixture.nativeElement.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(fixture.nativeElement.textContent).toContain('Session');
  });

  it('deletes one cookie', async () => {
    const fixture = await render();
    fixture.nativeElement.querySelector('[aria-label="Delete cookie session"]').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(store.deleteCookie).toHaveBeenCalledWith('dev.yaml', session);
    expect(fixture.nativeElement.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('clears every cookie after asking', async () => {
    const confirm = vi.fn().mockResolvedValue(false);
    dialogs.confirm = confirm;
    const fixture = await render();
    const clear = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLElement) =>
      b.textContent?.includes('Clear all'),
    ) as HTMLButtonElement;

    clear.click();
    await fixture.whenStable();
    expect(store.clearCookies).not.toHaveBeenCalled();

    confirm.mockResolvedValue(true);
    clear.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(store.clearCookies).toHaveBeenCalledWith('dev.yaml');
    expect(fixture.nativeElement.textContent).toContain('No cookies yet');
  });
});
