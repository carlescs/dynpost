import { DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { QuickOpenItemDto } from '../../models/messages';
import { Bridge } from '../../services/bridge';
import { WorkspaceStore } from '../../services/workspace-store';
import { QuickOpen } from './quick-open';

const item = (name: string, path: string, nameMatches: number[] = []): QuickOpenItemDto => ({
  path,
  name,
  method: 'GET',
  url: '{{baseUrl}}/x',
  nameMatches,
  pathMatches: [],
  urlMatches: [],
});

describe('QuickOpen', () => {
  let send: Mock;
  let close: Mock;

  // jsdom doesn't lay anything out, so it has no scrollIntoView.
  beforeEach(() => (Element.prototype.scrollIntoView = vi.fn()));

  async function render() {
    close = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: DialogRef, useValue: { close } },
      ],
    });
    const openRequest = vi.spyOn(TestBed.inject(WorkspaceStore), 'openRequest').mockResolvedValue();
    const fixture = TestBed.createComponent(QuickOpen);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector('input')!;
    const options = () => [...host.querySelectorAll('[role="option"]')];
    const type = async (value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const key = async (key: string) => {
      input.dispatchEvent(new KeyboardEvent('keydown', { key, cancelable: true }));
      await fixture.whenStable();
    };
    return { fixture, host, options, type, key, openRequest };
  }

  it('lists the requests the host finds, highlighting the matched characters', async () => {
    send = vi.fn(async (_type: string, { query }: { query: string }) =>
      query ? [item('Get user', 'requests/get-user.yaml', [0, 4])] : [],
    );
    const { host, options, type } = await render();
    expect(send).toHaveBeenCalledWith('workspace.quickOpen', { query: '' });

    await type(' gu ');

    expect(send).toHaveBeenLastCalledWith('workspace.quickOpen', { query: 'gu' });
    expect(options()).toHaveLength(1);
    expect([...host.querySelectorAll('mark')].map((m) => m.textContent)).toEqual(['G', 'u']);
  });

  it('moves the selection with the arrows and opens the selected request with Enter', async () => {
    send = vi
      .fn()
      .mockResolvedValue([
        item('Get user', 'requests/get-user.yaml'),
        item('Health', 'requests/health.yaml'),
      ]);
    const { options, key, openRequest } = await render();
    expect(options()[0].getAttribute('aria-selected')).toBe('true');

    await key('ArrowDown');
    expect(options()[1].getAttribute('aria-selected')).toBe('true');
    await key('ArrowDown');
    expect(options()[0].getAttribute('aria-selected')).toBe('true');
    await key('ArrowUp');
    await key('Enter');

    expect(close).toHaveBeenCalled();
    expect(openRequest).toHaveBeenCalledWith({
      path: 'requests/health.yaml',
      name: 'Health',
      kind: 'request',
    });
  });

  it('opens a request when it is clicked', async () => {
    send = vi.fn().mockResolvedValue([item('Health', 'requests/health.yaml')]);
    const { options, openRequest } = await render();

    (options()[0] as HTMLElement).click();

    expect(openRequest).toHaveBeenCalledWith(
      expect.objectContaining({ path: 'requests/health.yaml' }),
    );
  });

  it('keeps the answer to the latest query when an older one arrives last', async () => {
    let answerFirst!: (items: QuickOpenItemDto[]) => void;
    send = vi
      .fn()
      .mockResolvedValueOnce([])
      .mockReturnValueOnce(new Promise((resolve) => (answerFirst = resolve)))
      .mockResolvedValueOnce([item('Newer', 'requests/newer.yaml')]);
    const { fixture, host, type } = await render();

    await type('o');
    await type('ne');
    answerFirst([item('Older', 'requests/older.yaml')]);
    await fixture.whenStable();

    expect(host.textContent).toContain('Newer');
    expect(host.textContent).not.toContain('Older');
  });

  it('says when nothing matches', async () => {
    send = vi.fn().mockResolvedValue([]);
    const { host } = await render();

    expect(host.textContent).toContain('No matching requests.');
  });
});
