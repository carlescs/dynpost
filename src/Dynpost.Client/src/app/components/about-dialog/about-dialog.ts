import { DialogRef } from '@angular/cdk/dialog';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCode, faGlobe, faPaperPlane, faXmark } from '@fortawesome/free-solid-svg-icons';
import { AboutInfo, AboutLink } from '../../models/messages';
import { Bridge } from '../../services/bridge';

/**
 * The About page: the app version, the platform it runs on and links to the project's homepage
 * and source code. Opened with `Dialogs.open`.
 */
@Component({
  selector: 'app-about-dialog',
  imports: [FaIconComponent],
  templateUrl: './about-dialog.html',
})
export class AboutDialog implements OnInit {
  private readonly bridge = inject(Bridge);

  protected readonly ref = inject(DialogRef);
  protected readonly info = signal<AboutInfo | null>(null);
  protected readonly failed = signal(false);
  protected readonly linkError = signal<string | null>(null);
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faXmark = faXmark;
  protected readonly faGlobe = faGlobe;
  protected readonly faCode = faCode;

  async ngOnInit(): Promise<void> {
    try {
      this.info.set(await this.bridge.send('about.get'));
    } catch {
      this.failed.set(true);
    }
  }

  /** Opens a link in the system browser: the webview itself must not navigate away. */
  protected async openLink(event: Event, link: AboutLink): Promise<void> {
    event.preventDefault();
    this.linkError.set(null);
    try {
      await this.bridge.send('about.openLink', { link });
    } catch (error) {
      this.linkError.set(error instanceof Error ? error.message : 'Could not open the browser.');
    }
  }
}
