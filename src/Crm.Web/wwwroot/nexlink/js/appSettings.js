(() => {
    'use strict';
	
	const APP_SIDEBAR_BREAKPOINT = 1191;
	var docEl = document.documentElement;

	// App settings default
	const storedTheme = (document.cookie.match(/(?:^|; )theme=([^;]+)/) || [])[1];
	let appSettings = {
		appTheme: storedTheme === 'dark' || storedTheme === 'light' ? storedTheme : 'light',
		appSidebar: 'full',
		appColor: 'blue',
	};

	// Update settings
	function setAppSettings(newSettings = {}) {
		appSettings = {
			...appSettings,
			...newSettings
		};
		applySettings();
	}

	// Apply settings to DOM
	function applySettings() {
		docEl.setAttribute("data-bs-theme", appSettings.appTheme);

		if (window.innerWidth >= APP_SIDEBAR_BREAKPOINT) {
			docEl.setAttribute("data-app-sidebar", appSettings.appSidebar);
		}

		docEl.setAttribute("data-color-theme", appSettings.appColor);
	}

	// Initialize
	document.addEventListener("DOMContentLoaded", applySettings);
	window.setAppSettings = setAppSettings;

})();