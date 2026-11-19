import { Component, computed, input } from '@angular/core';
import { IS_MAC, KeyCombo, comboKeys, formatCombo } from '../../models/shortcuts';

/**
 * A keyboard shortcut drawn as keycaps, one per key (`Ctrl` + `Shift` + `Tab`, `⌘⇧⇥` on macOS).
 * The caps take their colors from the surrounding text, so it fits light menus and the dark header.
 */
@Component({
  selector: 'app-key-combo',
  templateUrl: './key-combo.html',
  host: { class: 'inline-flex' },
})
export class KeyComboView {
  readonly combo = input.required<KeyCombo>();
  /** Whether to use the macOS labels; follows the running platform unless set. */
  readonly mac = input(IS_MAC);

  protected readonly keys = computed(() => comboKeys(this.combo(), this.mac()));
  protected readonly label = computed(() => formatCombo(this.combo(), this.mac()));
}
