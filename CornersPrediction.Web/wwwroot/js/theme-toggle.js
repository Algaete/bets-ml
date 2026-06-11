(() => {
  const storageKey = 'app-theme';
  const root = document.documentElement;
  const toggle = document.querySelector('[data-theme-toggle]');
  const label = document.querySelector('[data-theme-toggle-label]');
  const icon = document.querySelector('[data-theme-toggle-icon]');

  const normalizeTheme = theme => theme === 'light' ? 'light' : 'dark';

  const applyTheme = theme => {
    const normalizedTheme = normalizeTheme(theme);
    root.setAttribute('data-theme', normalizedTheme);
    localStorage.setItem(storageKey, normalizedTheme);

    if (label) {
      label.textContent = normalizedTheme === 'dark' ? 'Modo claro' : 'Modo oscuro';
    }

    if (icon) {
      icon.textContent = normalizedTheme === 'dark' ? 'L' : 'D';
    }

    if (toggle) {
      toggle.setAttribute('aria-pressed', (normalizedTheme === 'light').toString());
      toggle.setAttribute(
        'title',
        normalizedTheme === 'dark' ? 'Cambiar a modo claro' : 'Cambiar a modo oscuro');
    }
  };

  applyTheme(root.getAttribute('data-theme') || localStorage.getItem(storageKey) || 'dark');

  toggle?.addEventListener('click', () => {
    const nextTheme = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    applyTheme(nextTheme);
  });
})();
