import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { OAuth2TokenDto } from '../../../../models/messages';
import { EMPTY_AUTH } from '../../../../models/request-mapping';
import { RequestAuth } from '../../../../models/workspace';
import { Bridge, BridgeError } from '../../../../services/bridge';
import { EnvironmentStore } from '../../../../services/environment-store';
import { OAuth2Auth } from './oauth2-auth';

const auth: RequestAuth = {
  ...EMPTY_AUTH,
  type: 'oauth2',
  tokenUrl: 'https://auth.test/token',
  clientId: 'app',
};

const token: OAuth2TokenDto = {
  obtained: '2026-11-07T12:00:00Z',
  expires: '2026-11-07T13:00:00Z',
  expired: false,
  refreshable: false,
  scope: 'read',
};

describe('OAuth2Auth', () => {
  let send: ReturnType<typeof vi.fn>;
  const activeId = signal<string | null>('dev.yaml');

  async function render(value: RequestAuth = auth) {
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { send } },
        { provide: EnvironmentStore, useValue: { activeId } },
      ],
    });
    const fixture = TestBed.createComponent(OAuth2Auth);
    fixture.componentRef.setInput('auth', value);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(300);
    fixture.detectChanges();
    return fixture;
  }

  function button(fixture: { nativeElement: HTMLElement }, label: string): HTMLButtonElement {
    return [...fixture.nativeElement.querySelectorAll('button')].find((b) =>
      b.textContent!.includes(label),
    ) as HTMLButtonElement;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    send = vi.fn().mockResolvedValue(null);
  });

  afterEach(() => vi.useRealTimers());

  it('shows the cached token of the selected environment', async () => {
    send.mockResolvedValue(token);
    const fixture = await render();

    expect(send).toHaveBeenCalledWith('oauth2.token.status', {
      auth: expect.objectContaining({ type: 'oauth2', clientId: 'app' }),
    });
    expect(fixture.nativeElement.textContent).toContain('Token valid until');
    expect(fixture.nativeElement.textContent).toContain('read');
  });

  it('says a client credentials token is obtained on send', async () => {
    const fixture = await render();

    expect(fixture.nativeElement.textContent).toContain('one is obtained when you send');
  });

  it('only shows the authorization code fields for that grant', async () => {
    let fixture = await render();
    expect(fixture.nativeElement.querySelector('#oauth2-auth-url')).toBeNull();

    TestBed.resetTestingModule();
    fixture = await render({ ...auth, grant: 'authorizationCode' });
    expect(fixture.nativeElement.querySelector('#oauth2-auth-url')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#oauth2-redirect')).not.toBeNull();
  });

  it('emits field changes', async () => {
    const fixture = await render();
    const changes: Partial<RequestAuth>[] = [];
    fixture.componentInstance.authChange.subscribe((c) => changes.push(c));

    const scope = fixture.nativeElement.querySelector('#oauth2-scope') as HTMLInputElement;
    scope.value = 'read write';
    scope.dispatchEvent(new Event('input'));

    expect(changes).toEqual([{ scope: 'read write' }]);
  });

  it('gets a new token', async () => {
    const fixture = await render();
    send.mockResolvedValue(token);

    button(fixture, 'Get new token').click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(send).toHaveBeenCalledWith('oauth2.token.get', {
      auth: expect.objectContaining({ tokenUrl: 'https://auth.test/token' }),
    });
    expect(fixture.nativeElement.textContent).toContain('Token valid until');
  });

  it('reports failures to get a token', async () => {
    const fixture = await render();
    send.mockRejectedValue(new BridgeError('oauth2.failed', 'The token endpoint answered 401.'));

    button(fixture, 'Get new token').click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('401');
  });

  it('clears the token', async () => {
    send.mockResolvedValue(token);
    const fixture = await render();
    send.mockResolvedValue(null);

    button(fixture, 'Clear').click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(send).toHaveBeenCalledWith('oauth2.token.clear', expect.anything());
    expect(fixture.nativeElement.textContent).toContain('No token yet');
  });

  it('reloads the status when the environment changes', async () => {
    await render();
    send.mockClear();

    activeId.set('prod.yaml');
    TestBed.tick();
    await vi.advanceTimersByTimeAsync(300);

    expect(send).toHaveBeenCalledWith('oauth2.token.status', expect.anything());
    activeId.set('dev.yaml');
  });
});
