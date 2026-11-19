import { TestBed } from '@angular/core/testing';
import { EMPTY_PLAN } from '../../models/plan-diff-model';
import { PlanForm } from '../../models/run-plan-form';
import { PlanDiff } from './plan-diff';

const plan = (patch: Partial<PlanForm>): PlanForm => ({ ...EMPTY_PLAN, ...patch });

function render(before: PlanForm, after: PlanForm): HTMLElement {
  const fixture = TestBed.createComponent(PlanDiff);
  fixture.componentRef.setInput('before', before);
  fixture.componentRef.setInput('after', after);
  fixture.detectChanges();
  return fixture.nativeElement;
}

describe('PlanDiff', () => {
  it('shows the sections either plan sets, with their changes', () => {
    const element = render(
      plan({ name: 'Smoke', requests: ['requests/a.yaml'], threads: '5' }),
      plan({ name: 'Soak', requests: ['requests/a.yaml'], threads: '50', duration: '10m' }),
    );

    const sections = [...element.querySelectorAll('section')];
    expect(sections.map((s) => s.getAttribute('aria-label'))).toEqual(['Requests', 'Load']);
    expect(sections[1].querySelector('h3')!.textContent).toContain('2');
    expect(element.querySelector('del')!.textContent).toContain('Smoke');
    expect(element.querySelector('ins')!.textContent).toContain('Soak');
    expect(element.textContent).toContain('3 difference(s).');
    expect(element.textContent).toContain('Not set in either plan: Environments, Data, Bodies');
  });

  it('says when the plans are the same', () => {
    const same = plan({ name: 'Smoke', requests: ['requests/a.yaml'] });

    expect(render(same, same).textContent).toContain('The plans are the same.');
  });
});
