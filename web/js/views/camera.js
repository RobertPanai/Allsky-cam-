// Schermata Camera: scelta della camera, guadagno, pose di notte e di giorno, binning, intervallo, cattura diurna.
import { api } from '../api.js';
import { t, errorMessage } from '../i18n.js';
import {
  h, clone, sameValue, withDefaults, setPath, inputField, selectField, switchField,
  cameraPicker, formSection, fieldGrid, saveBar, validateAll, applyServerErrors,
} from '../form-kit.js';

// Bozza della schermata: resta anche dopo un ridisegno (per esempio il cambio lingua),
// finché la configurazione salvata non cambia.
let session = null;

export function mount(root, ctx) {
  if (!ctx.config) {
    root.replaceChildren(h('p', { class: 'notice', role: 'alert', text: t('errors.network') }));
    return null;
  }
  if (!session || session.source !== ctx.config) {
    const base = withDefaults(ctx.config);
    session = { source: ctx.config, draft: clone(base), baseline: base };
  }
  const s = session;
  const { draft } = s;
  const fields = [];
  const add = (field) => {
    fields.push(field);
    return field;
  };

  const refreshDirty = () => bar.setDirty(!sameValue(draft, s.baseline));

  const picker = cameraPicker({
    selectedId: draft.camera?.id ?? '',
    connected: connectedCamera(ctx),
    onConnected: (id) => {
      setPath(draft, 'camera.id', id);
      refreshDirty();
    },
  });

  const onChange = () => refreshDirty();
  const gain = add(inputField({
    draft, path: 'camera.gain', kind: 'number', integer: true, onChange,
    label: t('camera.gain.label'), help: t('camera.gain.help'), min: 0, max: 1000,
  }));
  const maxExposure = add(inputField({
    draft, path: 'camera.maxExposureMs', kind: 'number', scale: 1000, unit: 's', onChange,
    label: t('camera.maxExposure.label'), help: t('camera.maxExposure.help'), min: 0.5, max: 600,
  }));
  const dayExposure = add(inputField({
    draft, path: 'camera.dayExposureMs', kind: 'number', unit: 'ms', onChange,
    label: t('camera.dayExposure.label'), help: t('camera.dayExposure.help'), min: 0.1, max: 1000,
  }));
  const binning = add(selectField({
    draft, path: 'camera.binning', numeric: true, onChange,
    label: t('camera.binning.label'), help: t('camera.binning.help'),
    options: [1, 2, 3, 4].map((n) => ({ value: n, label: `${n} × ${n}` })),
  }));
  const interval = add(inputField({
    draft, path: 'capture.intervalSeconds', kind: 'number', integer: true, unit: 's', onChange,
    label: t('camera.interval.label'), help: t('camera.interval.help'), min: 5, max: 3600,
  }));
  const dayCapture = add(switchField({
    draft, path: 'capture.dayCapture', onChange,
    label: t('camera.dayCapture.label'), help: t('camera.dayCapture.help'),
  }));

  const bar = saveBar({ onSave: save });

  async function save() {
    if (!validateAll(fields)) {
      bar.say(t('forms.invalid'), 'error');
      return;
    }
    bar.setBusy(true);
    bar.say('');
    try {
      const payload = clone(draft);
      const res = await api.saveConfig(payload);
      if (res && res.ok === false) {
        const rest = applyServerErrors(fields, res.errors);
        bar.say(rest.join(' ') || t('forms.invalid'), 'error');
        return;
      }
      s.baseline = clone(payload);
      ctx.setConfig(clone(payload));
      bar.say(t('forms.saved'), 'ok');
      refreshDirty();
    } catch (err) {
      bar.say(errorMessage(err), 'error');
    } finally {
      bar.setBusy(false);
    }
  }

  const section = h('section', { class: 'form-page', 'aria-labelledby': 'camera-title' },
    h('header', { class: 'page-head' },
      h('h1', { id: 'camera-title', class: 'page-title', tabindex: '-1', text: t('camera.title') }),
      h('p', { class: 'lede', text: t('camera.lede') }),
    ),
    formSection({ title: t('camera.sections.camera') }, picker.node),
    formSection({ title: t('camera.sections.exposure') },
      fieldGrid(gain.node, maxExposure.node, dayExposure.node),
    ),
    formSection({ title: t('camera.sections.capture') },
      fieldGrid(binning.node, interval.node),
      dayCapture.node,
    ),
    bar.node,
  );

  root.replaceChildren(section);
  picker.init();
  refreshDirty();

  return {
    unmount() {
      picker.destroy();
    },
  };
}

function connectedCamera(ctx) {
  const cam = ctx.getStatus()?.camera;
  return cam?.connected && cam.id ? { id: cam.id, name: cam.name } : null;
}
