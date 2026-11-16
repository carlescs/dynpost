import { DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../services/bridge';
import { AboutDialog } from './about-dialog';

const ABOUT = {
  version: '1.2.3',
  runtime: '.NET 10',
  operatingSystem: 'Linux',
  homepage: 'https://carlescs.github.io/dynpost/',
  repository: 'https://github.com/carlescs/dynpost',
};

describe('AboutDialog', () => {
  async function create(send: ReturnType<typeof vi.fn>) {
    TestBed.configureTestingModule({
      imports: [AboutDialog],
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: DialogRef, useValue: { close: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(AboutDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('opens the project links in the system browser', async () => {
    const send = vi
      .fn()
      .mockImplementation((type: string) => Promise.resolve(type === 'about.get' ? ABOUT : null));
    const fixture = await create(send);
    const element: HTMLElement = fixture.nativeElement;
    const homepage = element.querySelector<HTMLAnchorElement>('[data-testid="about-homepage"]')!;
    const repository = element.querySelector<HTMLAnchorElement>(
      '[data-testid="about-repository"]',
    )!;

    expect(homepage.getAttribute('href')).toBe(ABOUT.homepage);
    expect(repository.getAttribute('href')).toBe(ABOUT.repository);
    const click = new MouseEvent('click', { cancelable: true });
    repository.dispatchEvent(click);
    homepage.click();

    expect(click.defaultPrevented).toBe(true);
    expect(send).toHaveBeenCalledWith('about.openLink', { link: 'repository' });
    expect(send).toHaveBeenCalledWith('about.openLink', { link: 'homepage' });
  });

  it('shows the version from the host', async () => {
    const send = vi.fn().mockResolvedValue(ABOUT);
    TestBed.configureTestingModule({
      imports: [AboutDialog],
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: DialogRef, useValue: { close: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(AboutDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(send).toHaveBeenCalledWith('about.get');
    expect(
      fixture.nativeElement.querySelector('[data-testid="about-version"]').textContent,
    ).toContain('1.2.3');
  });
});
