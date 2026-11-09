import { TestBed } from '@angular/core/testing';
import { Bridge } from '../../services/bridge';
import { AboutDialog } from './about-dialog';

describe('AboutDialog', () => {
  it('shows the version from the host', async () => {
    const send = vi
      .fn()
      .mockResolvedValue({ version: '1.2.3', runtime: '.NET 10', operatingSystem: 'Linux' });
    TestBed.configureTestingModule({
      imports: [AboutDialog],
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
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
