import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

// Bootstrap i18next with an empty resource set; the SETTING module loads each
// language pack from the backend on demand (see settingsOperations.loadAndApplyLanguage).
void i18n.use(initReactI18next).init({
  resources: { en: { translation: {} } },
  lng: 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  // {{key}} placeholders match the backend language packs.
});

export default i18n;
