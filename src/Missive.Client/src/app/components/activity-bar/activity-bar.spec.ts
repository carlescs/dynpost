import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Bridge } from '../../services/bridge';
import { WorkspaceStore } from '../../services/workspace-store';
import { ActivityBar } from './activity-bar';

@Component({ template: '' })
class Panel {}

describe('ActivityBar', () => {
  let router: Router;
  let store: WorkspaceStore;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(
          ['explorer', 'environments', 'git'].map((path) => ({ path, component: Panel })),
        ),
        {
          provide: Bridge,
          useValue: { isDesktop: true, send: vi.fn(() => new Promise(() => {})) },
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
    return { button, click };
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
});
