import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Bridge } from '../../services/bridge';
import { WorkspaceStore } from '../../services/workspace-store';
import { ActivityBar, orderActivities } from './activity-bar';

@Component({ template: '' })
class Panel {}

describe('ActivityBar', () => {
  let router: Router;
  let store: WorkspaceStore;
  let send: (...args: unknown[]) => Promise<unknown>;

  beforeEach(async () => {
    send = vi.fn(() => new Promise<unknown>(() => {}));
    TestBed.configureTestingModule({
      providers: [
        provideRouter(
          ['explorer', 'environments', 'git'].map((path) => ({ path, component: Panel })),
        ),
        {
          provide: Bridge,
          useValue: { isDesktop: true, send: (...args: unknown[]) => send(...args) },
        },
      ],
    });
    router = TestBed.inject(Router);
    store = TestBed.inject(WorkspaceStore);
    await router.navigateByUrl('/explorer');
  });

  async function render() {
    const fixture = TestBed.createComponent(ActivityBar);
    await fixture.whenStable();
    const button = (label: string) =>
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
        `[aria-label="${label}"]`,
      )!;
    const click = async (label: string) => {
      button(label).click();
      await fixture.whenStable();
    };
    const labels = () =>
      [...(fixture.nativeElement as HTMLElement).querySelectorAll('button[data-activity]')].map(
        (b) => b.getAttribute('aria-label'),
      );
    return { fixture, button, click, labels };
  }

  it('marks the routed activity as active', async () => {
    const { button } = await render();
    expect(button('Explorer').getAttribute('aria-pressed')).toBe('true');
    expect(button('Source control').getAttribute('aria-pressed')).toBe('false');
  });

  it('routes to another activity and opens the sidebar', async () => {
    store.toggleSidebar(false);
    const { button, click } = await render();
    await click('Source control');
    expect(router.url).toBe('/git');
    expect(store.sidebarOpen()).toBe(true);
    expect(button('Source control').getAttribute('aria-pressed')).toBe('true');
    expect(button('Explorer').getAttribute('aria-pressed')).toBe('false');
  });

  it('toggles the sidebar when the routed activity is clicked again', async () => {
    const { button, click } = await render();
    await click('Explorer');
    expect(router.url).toBe('/explorer');
    expect(store.sidebarOpen()).toBe(false);
    expect(button('Explorer').getAttribute('aria-pressed')).toBe('false');
    await click('Explorer');
    expect(store.sidebarOpen()).toBe(true);
  });

  it('shows the activities in the saved order', async () => {
    send = vi.fn().mockResolvedValue({ activityOrder: ['git', 'removed', 'runs'] });
    const { fixture, labels } = await render();
    await fixture.whenStable();
    expect(labels()).toEqual(['Source control', 'Load runs', 'Explorer', 'Environments']);
  });

  it('moves the focused activity with Alt+arrows and saves the order', async () => {
    send = vi.fn().mockResolvedValue({ activityOrder: [] });
    const { fixture, button, labels } = await render();
    const key = async (label: string, key: string) => {
      button(label).dispatchEvent(
        new KeyboardEvent('keydown', { key, altKey: true, bubbles: true, cancelable: true }),
      );
      await fixture.whenStable();
    };

    await key('Load runs', 'ArrowUp');
    expect(labels()).toEqual(['Explorer', 'Load runs', 'Environments', 'Source control']);
    await key('Explorer', 'ArrowUp');
    expect(labels()).toEqual(['Explorer', 'Load runs', 'Environments', 'Source control']);
    await key('Explorer', 'ArrowDown');
    expect(labels()).toEqual(['Load runs', 'Explorer', 'Environments', 'Source control']);
    await vi.waitFor(() =>
      expect(send).toHaveBeenLastCalledWith('settings.setActivityOrder', {
        order: ['runs', 'explorer', 'environments', 'git'],
      }),
    );
    expect(router.url).toBe('/explorer');
  });
});

describe('ActivityBar reset', () => {
  it('restores the default order and saves an empty order', async () => {
    const send = vi.fn().mockResolvedValue({ activityOrder: ['git', 'runs'] });
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: Bridge, useValue: { isDesktop: true, send } }],
    });
    const fixture = TestBed.createComponent(ActivityBar);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const reset = host.querySelector<HTMLButtonElement>(
      '[aria-label="Reset the order of the views"]',
    )!;
    const labels = () =>
      [...host.querySelectorAll('button[data-activity]')].map((b) => b.getAttribute('aria-label'));
    expect(labels()[0]).toBe('Source control');
    expect(reset.disabled).toBe(false);

    reset.click();
    await fixture.whenStable();

    expect(labels()).toEqual(['Explorer', 'Environments', 'Load runs', 'Source control']);
    expect(reset.disabled).toBe(true);
    await vi.waitFor(() =>
      expect(send).toHaveBeenLastCalledWith('settings.setActivityOrder', { order: [] }),
    );
  });
});

describe('orderActivities', () => {
  it('drops unknown and repeated ids and appends the missing activities', () => {
    expect(orderActivities(['runs', 'nope', 'runs', 'git'])).toEqual([
      'runs',
      'git',
      'explorer',
      'environments',
    ]);
    expect(orderActivities([])).toEqual(['explorer', 'environments', 'runs', 'git']);
  });
});
