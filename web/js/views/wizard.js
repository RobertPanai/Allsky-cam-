// Procedura guidata del primo avvio: Camera, Posizione, Cartella e archivio. Il salvataggio avviene alla fine.
import { api } from '../api.js';
import { t, errorMessage } from '../i18n.js';
import {
  h, clone, setPath, validateAll, applyServerErrors, cameraPicker, siteFields, archiveFields,
  productFields, locationTool, fieldGrid,
} from '../form-kit.js';

const STEP_KEYS = ['camera', 'site', 'archive'];
const LAST = STEP_KEYS.length - 1;

export function mount(root, ctx) {
  if (!ctx.config) {
    const p = h('p', { class: 'notice', role: 'alert', text: t('errors.network') });
    root.replaceChildren(p);
    return null;
  }

  // Copia di lavoro: nulla va al motore finché non si preme Fine.
  const draft = clone(ctx.config);
  draft.site = draft.site || {};
  if (!draft.site.timezone) {
    draft.site.timezone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  }

  let step = 0;
  let picker = null;
  let rendered = false;

  // Campi costruiti una volta sola: gli errori del motore restano visibili anche cambiando passo.
  const stepFields = [[], siteFields(draft), [...archiveFields(draft), ...productFields(draft)]];
  const locate = locationTool({
    onLocated(loc) {
      setPath(draft, 'site.latitude', loc.latitude);
      setPath(draft, 'site.longitude', loc.longitude);
      if (loc.elevation !== undefined) setPath(draft, 'site.elevationM', loc.elevation);
      stepFields[1].forEach((f) => f.refresh());
    },
  });

  const titleEl = h('h1', { class: 'page-title', id: 'wizard-title', tabindex: '-1' });
  const ledeEl = h('p', { class: 'lede' });
  const body = h('div', { class: 'wizard-body' });
  const note = h('p', { class: 'form-status', role: 'status' });
  const stepsList = h('ol', { class: 'steps', 'aria-label': t('wizard.progress') });
  const fill = h('div', { class: 'steps-fill' });
  const backBtn = h('button', { type: 'button', class: 'btn' }, t('wizard.back'));
  const nextBtn = h('button', { type: 'button', class: 'btn btn-primary' });
  const skipBtn = h('button', { type: 'button', class: 'btn-text' }, t('wizard.skip'));

  const section = h('section', { class: 'wizard', 'aria-labelledby': 'wizard-title' },
    h('div', { class: 'steps-wrap' },
      stepsList,
      h('div', { class: 'steps-track', 'aria-hidden': 'true' }, fill),
    ),
    h('div', { class: 'wizard-head' }, titleEl, ledeEl),
    body,
    note,
    h('footer', { class: 'wizard-foot' },
      h('div', { class: 'btn-row' }, backBtn, nextBtn),
      h('div', { class: 'wizard-skip' },
        skipBtn,
        h('p', { class: 'field-help', text: t('wizard.skipHelp') }),
      ),
    ),
  );

  function connectedCamera() {
    const cam = ctx.getStatus()?.camera;
    return cam?.connected && cam.id ? { id: cam.id, name: cam.name } : null;
  }

  function renderProgress() {
    stepsList.replaceChildren(...STEP_KEYS.map((key, i) => {
      const cls = ['step'];
      if (i === step) cls.push('is-current');
      if (i < step) cls.push('is-done');
      return h('li', { class: cls.join(' '), 'aria-current': i === step ? 'step' : undefined },
        h('span', { class: 'step-num', text: String(i + 1) }),
        h('span', { class: 'step-name', text: t(`wizard.steps.${key}`) }),
      );
    }));
    fill.style.width = `${(step / LAST) * 100}%`;
  }

  function showStep(n) {
    step = n;
    picker?.destroy();
    picker = null;
    note.textContent = '';
    note.classList.remove('is-error');

    const key = STEP_KEYS[n];
    titleEl.textContent = t(`wizard.${key}.title`);
    ledeEl.textContent = t(`wizard.${key}.lede`);
    body.replaceChildren();

    if (n === 0) {
      picker = cameraPicker({
        selectedId: draft.camera?.id ?? '',
        connected: connectedCamera(),
        onConnected: (id) => setPath(draft, 'camera.id', id),
      });
      body.append(picker.node);
      picker.init();
    } else if (n === 1) {
      body.append(locate.node, fieldGrid(...stepFields[1].map((f) => f.node)));
    } else {
      const products = stepFields[2].slice(2);
      body.append(
        h('div', { class: 'stack' },
          ...stepFields[2].slice(0, 2).map((f) => f.node),
          h('fieldset', { class: 'field-group' },
            h('legend', { class: 'section-title', text: t('forms.fields.products.legend') }),
            ...products.map((f) => f.node),
          ),
        ),
      );
    }

    renderProgress();
    backBtn.hidden = n === 0;
    nextBtn.textContent = n === LAST ? t('wizard.finish') : t('wizard.next');
    if (rendered) titleEl.focus({ preventScroll: true });
    rendered = true;
  }

  function forward() {
    if (step === 1 && !validateAll(stepFields[1])) {
      note.textContent = t('forms.invalid');
      note.classList.add('is-error');
      return;
    }
    if (step < LAST) {
      showStep(step + 1);
      return;
    }
    finish();
  }

  async function finish() {
    // Controllo di tutti i passi: se uno non va, si torna a quello.
    const bad = [1, 2].find((i) => !stepFields[i].map((f) => f.validate()).every(Boolean));
    if (bad !== undefined) {
      showStep(bad);
      validateAll(stepFields[bad]);
      note.textContent = t('forms.invalid');
      note.classList.add('is-error');
      return;
    }

    nextBtn.disabled = true;
    note.textContent = t('forms.saving');
    note.classList.remove('is-error');
    try {
      const res = await api.saveConfig(clone(draft));
      if (res && res.ok === false) {
        const fields = [...stepFields[1], ...stepFields[2]];
        const rest = applyServerErrors(fields, res.errors);
        const firstPath = res.errors?.[0]?.path;
        const where = [1, 2].find((i) => stepFields[i].some((f) => f.path === firstPath));
        if (where !== undefined) {
          showStep(where);
          stepFields[where].find((f) => f.path === firstPath)?.focus();
        }
        note.textContent = rest.join(' ') || t('forms.invalid');
        note.classList.add('is-error');
        return;
      }
      ctx.setConfig(clone(draft));
      ctx.setWizardDone(true);
      ctx.navigate('#/live');
    } catch (err) {
      note.textContent = errorMessage(err);
      note.classList.add('is-error');
    } finally {
      nextBtn.disabled = false;
    }
  }

  backBtn.addEventListener('click', () => {
    if (step > 0) showStep(step - 1);
  });
  nextBtn.addEventListener('click', forward);
  // Salta la procedura senza salvare: si può rifarla dalla schermata Sito.
  skipBtn.addEventListener('click', () => {
    ctx.setWizardDone(true);
    ctx.navigate('#/live');
  });

  root.replaceChildren(section);
  showStep(0);

  return {
    unmount() {
      picker?.destroy();
    },
  };
}
