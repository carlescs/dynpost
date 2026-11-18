import { TestBed } from '@angular/core/testing';
import { InfoTip } from './info-tip';

describe('InfoTip', () => {
  async function create(text: string) {
    const fixture = TestBed.createComponent(InfoTip);
    fixture.componentRef.setInput('text', text);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the explanation as a tooltip and to screen readers', async () => {
    const host = await create('How many users send requests at once');

    expect(host.getAttribute('title')).toBe('How many users send requests at once');
    expect(host.getAttribute('aria-label')).toBe('How many users send requests at once');
    expect(host.getAttribute('tabindex')).toBe('0');
    expect(host.querySelector('svg')).not.toBeNull();
  });

  it("doesn't let a click reach the label around it", async () => {
    const host = await create('Info');
    const click = new MouseEvent('click', { bubbles: true, cancelable: true });

    host.dispatchEvent(click);

    expect(click.defaultPrevented).toBe(true);
  });
});
