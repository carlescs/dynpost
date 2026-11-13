import { DialogRef } from '@angular/cdk/dialog';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faPaperPlane, faXmark } from '@fortawesome/free-solid-svg-icons';
import { AboutInfo } from '../../models/messages';
import { Bridge } from '../../services/bridge';

/** The About page: the app version and the platform it runs on. Opened with `Dialogs.open`. */
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
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faXmark = faXmark;

  async ngOnInit(): Promise<void> {
    try {
      this.info.set(await this.bridge.send('about.get'));
    } catch {
      this.failed.set(true);
    }
  }
}
