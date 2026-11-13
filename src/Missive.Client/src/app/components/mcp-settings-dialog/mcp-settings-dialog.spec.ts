import { DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { McpStatusDto } from '../../models/messages';
import { Bridge, BridgeError } from '../../services/bridge';
import { Dialogs } from '../../services/dialogs';
import { McpSettingsDialog } from './mcp-settings-dialog';

describe('McpSettingsDialog', () => {
  const off: McpStatusDto = {
    enabled: false,
    running: false,
    port: 47823,
    url: 'http://localhost:47823/mcp',
    token: null,
  };
  const on: McpStatusDto = { ...off, enabled: true, running: true, token: 'abc123' };

  async function setup(initial: McpStatusDto, confirm = true) {
    const send = vi.fn().mockResolvedValue(initial);
    const confirmed = vi.fn().mockResolvedValue(confirm);
    TestBed.configureTestingModule({
      imports: [McpSettingsDialog],
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: DialogRef, useValue: { close: vi.fn() } },
        { provide: Dialogs, useValue: { confirm: confirmed } },
      ],
    });
    const fixture = TestBed.createComponent(McpSettingsDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const settle = async () => {
      await fixture.whenStable();
      fixture.detectChanges();
    };
    const button = (label: string) =>
      [...root.querySelectorAll('button')].find(
        (b) => b.getAttribute('aria-label') === label || b.textContent?.trim() === label,
      )!;
    return { root, send, confirmed, settle, button };
  }

  it('turns the server on and shows how to connect a client', async () => {
    const { root, send, settle } = await setup(off);
    expect(root.querySelector('[role="switch"]')!.getAttribute('aria-checked')).toBe('false');
    expect(root.textContent).not.toContain('claude mcp add');

    send.mockResolvedValue(on);
    root.querySelector<HTMLButtonElement>('[role="switch"]')!.click();
    await settle();

    expect(send).toHaveBeenLastCalledWith('mcp.set', { enabled: true });
    expect(root.querySelector('[role="switch"]')!.getAttribute('aria-checked')).toBe('true');
    expect(root.querySelector('[role="status"]')!.textContent).toContain(
      'http://localhost:47823/mcp',
    );
    expect(root.textContent).toContain(
      'claude mcp add --transport http missive http://localhost:47823/mcp --header "Authorization: Bearer abc123"',
    );
    expect(root.querySelector('pre')!.textContent).toContain('"Authorization": "Bearer abc123"');
  });

  it('hides the port, token and client setup while the server is off', async () => {
    const { root, send, settle } = await setup(on);
    expect(root.querySelector('input[name="port"]')).not.toBeNull();

    // Turning the server off keeps its token for the next time it is turned on.
    send.mockResolvedValue({ ...on, enabled: false, running: false });
    root.querySelector<HTMLButtonElement>('[role="switch"]')!.click();
    await settle();

    expect(root.querySelector('[role="status"]')).toBeNull();
    expect(root.querySelector('input[name="port"]')).toBeNull();
    expect(root.querySelector('[data-testid="token"]')).toBeNull();
    expect(root.textContent).not.toContain('claude mcp add');
    expect(root.querySelector('pre')).toBeNull();
  });

  it('hides the token until asked', async () => {
    const { root, settle, button } = await setup(on);
    const token = () => root.querySelector('[data-testid="token"]')!.textContent;
    expect(token()).not.toContain('abc123');

    button('Show token').click();
    await settle();

    expect(token()).toContain('abc123');
  });

  it('says why a server that is on is not running', async () => {
    const { root } = await setup({
      ...on,
      running: false,
      error: 'Could not listen on port 47823: in use',
    });

    expect(root.querySelector('[role="status"]')!.textContent).toContain(
      'Not running: Could not listen on port 47823: in use',
    );
  });

  it('moves the server to another port, refusing ports out of range', async () => {
    const { root, send, settle, button } = await setup(on);
    const input = root.querySelector<HTMLInputElement>('input[name="port"]')!;

    input.value = '80';
    input.dispatchEvent(new Event('input'));
    await settle();
    button('Apply').click();
    await settle();
    expect(root.querySelector('[role="alert"]')!.textContent).toContain('between 1024 and 65535');
    expect(send).not.toHaveBeenCalledWith('mcp.set', expect.anything());

    send.mockResolvedValue({ ...on, port: 5000, url: 'http://localhost:5000/mcp' });
    input.value = '5000';
    input.dispatchEvent(new Event('input'));
    await settle();
    button('Apply').click();
    await settle();

    expect(send).toHaveBeenLastCalledWith('mcp.set', { enabled: true, port: 5000 });
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it('replaces the token only once confirmed', async () => {
    const { send, confirmed, settle, button } = await setup(on, false);

    button('Replace…').click();
    await settle();
    expect(confirmed).toHaveBeenCalled();
    expect(send).not.toHaveBeenCalledWith('mcp.regenerateToken');

    confirmed.mockResolvedValue(true);
    button('Replace…').click();
    await settle();
    expect(send).toHaveBeenLastCalledWith('mcp.regenerateToken');
  });

  it('shows errors from the host', async () => {
    const { root, send, settle } = await setup(off);
    send.mockRejectedValue(new BridgeError('settings.save_failed', 'Disk full'));

    root.querySelector<HTMLButtonElement>('[role="switch"]')!.click();
    await settle();

    expect(root.querySelector('[role="alert"]')!.textContent).toContain('Disk full');
  });
});
