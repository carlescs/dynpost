import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SharingToggle } from './sharing-toggle';

@Component({
  imports: [SharingToggle],
  template: `<app-sharing-toggle
    [localOnly]="localOnly()"
    name="Dev"
    (toggled)="toggled = toggled + 1"
  />`,
})
class Host {
  readonly localOnly = signal(false);
  toggled = 0;
}

describe('SharingToggle', () => {
  async function render(localOnly: boolean) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.localOnly.set(localOnly);
    await fixture.whenStable();
    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    return { fixture, button };
  }

  it('offers to keep a shared file local, showing only on hover', async () => {
    const { button } = await render(false);
    expect(button.getAttribute('aria-label')).toBe('Keep Dev local only');
    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(button.classList).toContain('opacity-0');
  });

  it('shows a local-only file and offers to share it', async () => {
    const { button } = await render(true);
    expect(button.getAttribute('aria-label')).toBe('Share Dev through git');
    expect(button.getAttribute('aria-pressed')).toBe('true');
    expect(button.classList).toContain('text-amber-600');
    expect(button.classList).not.toContain('opacity-0');
  });

  it('emits when clicked', async () => {
    const { fixture, button } = await render(false);
    button.click();
    expect(fixture.componentInstance.toggled).toBe(1);
  });
});
