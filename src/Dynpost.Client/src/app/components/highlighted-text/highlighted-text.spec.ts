import { TestBed } from '@angular/core/testing';
import { highlightPositions } from '../../models/highlight';
import { HighlightedText } from './highlighted-text';

describe('HighlightedText', () => {
  it('marks the matched parts without adding spaces between parts', async () => {
    const fixture = TestBed.createComponent(HighlightedText);
    fixture.componentRef.setInput('segments', highlightPositions('Create user', [7, 8]));
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toBe('Create user');
    expect(host.querySelector('mark')!.textContent).toBe('us');
  });
});
