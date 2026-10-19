// Schermata Sito: dati del sito, lingua (subito), tema (con anteprima), porta e rete, archivio, testo sulle immagini.
import { api } from '../api.js';
import { t, errorMessage, locale } from '../i18n.js';
import {
  h, clone, sameValue, withDefaults, setPath, inputField, selectField, switchField, choiceField,
  siteFields, archiveFields, locationTool, formSection, fieldGrid, saveBar, validateAll, applyServerErrors,
} from '../form-kit.js';

// Bozza della schermata: resta anche dopo un ridisegno (cambio lingua), finché la configurazione salvata non cambia.
let session = null;

const THEMES = ['auto', 'day', 'night', 'red'];

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
  const addAll = (list) => list.forEach(add);

  const refreshDirty = () => bar.setDirty(!sameValue(draft, s.baseline));
  const onChange = () => refreshDirty();

  // Dati del sito, con il pulsante per leggere la posizione del dispositivo.
  const siteList = siteFields(draft, onChange);
  addAll(siteList);
  const locate = locationTool({
    onLocated(loc) {
      setPath(draft, 'site.latitude', loc.latitude);
      setPath(draft, 'site.longitude', loc.longitude);
      if (loc.elevation !== undefined) setPath(draft, 'site.elevationM', loc.elevation);
      siteList.forEach((f) => f.refresh());
      refreshDirty();
    },
  });

  // Lingua: l'interfaccia cambia subito; il valore si salva con Salva.
  const language = add(selectField({
    draft, path: 'language', id: 'site-language',
    label: t('site.language.label'), help: t('site.language.help'),
    options: ['it', 'en'].map((code) => ({ value: code, label: t(`forms.languages.${code}`) })),
    onChange: () => {
      refreshDirty();
      if (draft.language !== locale()) ctx.setLanguage(draft.language);
    },
  }));

  // Tema: l'anteprima si applica a tutta la pagina finché non si salva o si lascia la schermata.
  const theme = add(choiceField({
    draft, path: 'ui.theme', layout: 'grid',
    legend: t('site.theme.label'), help: t('site.theme.help'),
    options: THEMES.map((value) => ({
      value,
      label: t(`site.theme.${value}.label`),
      help: t(`site.theme.${value}.help`),
    })),
    onChange: () => {
      ctx.previewTheme(draft.ui.theme);
      refreshDirty();
    },
  }));

  // Rete: la porta si applica al riavvio; l'accesso dalla rete locale è disabilitato finché non c'è una password.
  const port = add(inputField({
    draft, path: 'ui.port', kind: 'number', integer: true, onChange,
    label: t('site.port.label'), help: t('site.port.help'), min: 1024, max: 65535,
  }));
  const lan = add(switchField({
    draft, path: 'ui.allowLan', onChange,
    label: t('site.lan.label'), help: t('site.lan.help'),
    disabled: !draft.ui?.allowLan,
    disabledHelp: t('site.lan.disabled'),
    warning: t('site.lan.warning'),
  }));

  addAll(archiveFields(draft, onChange));

  // Testo sulle immagini.
  const overlayEnabled = add(switchField({
    draft, path: 'overlay.enabled', onChange,
    label: t('site.overlay.enabled.label'), help: t('site.overlay.enabled.help'),
  }));
  const overlayTime = add(switchField({
    draft, path: 'overlay.showTime', onChange,
    label: t('site.overlay.showTime.label'), help: t('site.overlay.showTime.help'),
  }));
  const overlayTemp = add(switchField({
    draft, path: 'overlay.showSensorTemp', onChange,
    label: t('site.overlay.showSensorTemp.label'), help: t('site.overlay.showSensorTemp.help'),
  }));
  const overlayLabel = add(inputField({
    draft, path: 'overlay.label', maxLength: 40, onChange,
    label: t('site.overlay.label.label'), help: t('site.overlay.label.help'),
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

  const section = h('section', { class: 'form-page', 'aria-labelledby': 'site-title' },
    h('header', { class: 'page-head' },
      h('h1', { id: 'site-title', class: 'page-title', tabindex: '-1', text: t('site.title') }),
      h('p', { class: 'lede', text: t('site.lede') }),
    ),
    formSection({ title: t('site.sections.data') },
      locate.node,
      fieldGrid(...siteList.map((f) => f.node)),
    ),
    formSection({ title: t('site.sections.language') },
      fieldGrid(language.node),
      theme.node,
    ),
    formSection({ title: t('site.sections.network') },
      fieldGrid(port.node),
      lan.node,
    ),
    formSection({ title: t('site.sections.archive') },
      fieldGrid(...fields.filter((f) => f.path === 'storage.folder' || f.path === 'storage.keepNights').map((f) => f.node)),
    ),
    formSection({ title: t('site.sections.overlay') },
      overlayEnabled.node,
      overlayTime.node,
      overlayTemp.node,
      fieldGrid(overlayLabel.node),
    ),
    h('p', { class: 'site-foot' },
      h('a', { class: 'text-link', href: '#/wizard', text: t('site.rerun') }),
    ),
    bar.node,
  );

  root.replaceChildren(section);
  // Il tema scelto si vede subito; alla chiusura della schermata torna quello salvato.
  ctx.previewTheme(draft.ui?.theme ?? null);
  refreshDirty();

  return {
    unmount() {
      ctx.previewTheme(null);
    },
  };
}
