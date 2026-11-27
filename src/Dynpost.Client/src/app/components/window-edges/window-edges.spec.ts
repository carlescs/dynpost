import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../services/bridge';
import { WindowFrame } from '../../services/window-frame';
import { WindowEdges } from './window-edges';

describe('WindowEdges', () => {
  it('resizes the window from the edge pressed', () => {
    const send = vi.fn().mockResolvedValue(null);
    TestBed.configureTestingModule({
      imports: [WindowEdges],
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
    });
    TestBed.inject(WindowFrame).state.set({
      frame: 'custom',
      platform: 'windows',
      maximized: false,
      tray: false,
    });
    const fixture = TestBed.createComponent(WindowEdges);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;

    const edges = [...root.querySelectorAll<HTMLElement>('[data-edge]')].map(
      (e) => e.dataset['edge'],
    );
    expect(edges).toEqual(['top', 'topLeft', 'topRight']);

    root
      .querySelector('[data-edge="topLeft"]')!
      .dispatchEvent(new MouseEvent('mousedown', { button: 0, cancelable: true }));
    expect(send).toHaveBeenCalledWith('window.startResize', { edge: 'topLeft' });
  });

  it('draws nothing with the system title bar', () => {
    TestBed.configureTestingModule({
      imports: [WindowEdges],
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send: vi.fn() } }],
    });
    const fixture = TestBed.createComponent(WindowEdges);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-edge]')).toBeNull();
  });
});
