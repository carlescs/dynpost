import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { Bridge } from './services/bridge';

describe('App', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        {
          provide: Bridge,
          useValue: {
            isDesktop: true,
            send: vi.fn().mockResolvedValue(null),
            on: vi.fn(() => () => {}),
          },
        },
      ],
    });
    TestBed.createComponent(App);
  });

  it('never lets the browser show its own context menu', () => {
    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });

    document.body.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
  });
});
