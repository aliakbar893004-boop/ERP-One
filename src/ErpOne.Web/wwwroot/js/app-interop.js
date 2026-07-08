// SweetAlert2 helpers dipanggil dari Blazor via IJSRuntime (SwalService).
window.appSwal = {
    confirm: async (title, text, confirmText) => {
        const result = await Swal.fire({
            title: title,
            text: text,
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: confirmText || 'Yes',
            cancelButtonText: 'Cancel',
            confirmButtonColor: '#dc3545',
            cancelButtonColor: '#6c757d',
            reverseButtons: true
        });
        return result.isConfirmed;
    },

    toast: (icon, title) => {
        Swal.fire({
            toast: true,
            position: 'top-end',
            icon: icon,
            title: title,
            showConfirmButton: false,
            timer: 2400,
            timerProgressBar: true
        });
    },

    alert: (title, text) => Swal.fire({
        title: title,
        text: text,
        icon: 'info',
        confirmButtonText: 'OK',
        confirmButtonColor: '#2563eb'
    })
};

// Smooth-scroll to an element by id (used by Atlas form section navigators).
// Plain anchor `#id` links can't be used because <base href="/"> resolves them
// to "/#id" and navigates Home.
window.pfScrollTo = (id) => {
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

window.appPrint = () => window.print();

// The sidebar is a CSS-only hover-to-expand icon rail — no JS state needed.

// Theme toggle (light default). State persisted to localStorage and applied as
// `data-bs-theme` on <html>; Bootstrap 5.3 themes its components off that, and
// our chrome dark overrides (scoped CSS) react to the same attribute. Static
// SSR layout, so this is plain JS re-applied after enhanced navigation.
(function () {
    const KEY = 'myapp.theme';
    function apply() {
        var dark = localStorage.getItem(KEY) === 'dark';
        document.documentElement.setAttribute('data-bs-theme', dark ? 'dark' : 'light');
    }
    document.addEventListener('click', function (e) {
        if (e.target.closest('#theme-toggle')) {
            var dark = localStorage.getItem(KEY) === 'dark';
            localStorage.setItem(KEY, dark ? 'light' : 'dark');
            apply();
        }
    });
    document.addEventListener('DOMContentLoaded', apply);
    (function registerEnhanced(attempts) {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', apply);
        } else if (attempts < 200) {
            setTimeout(function () { registerEnhanced(attempts + 1); }, 50);
        }
    })(0);
    apply();
})();

// Appearance: font size / font family / accent colour. Each choice is stored in
// localStorage and reflected as a data-* attribute on <html>; app.css maps those
// attributes to CSS variables (--app-font / --app-accent*) and a root font-size.
// Same static-SSR pattern as the theme toggle.
(function () {
    const KEYS = {
        'font-size': { ls: 'myapp.fontSize', attr: 'data-font-size', def: 'md' },
        'font':      { ls: 'myapp.font',     attr: 'data-font',      def: 'plex' },
        'accent':    { ls: 'myapp.accent',   attr: 'data-accent',    def: 'emerald' }
    };
    function apply() {
        Object.keys(KEYS).forEach(function (set) {
            var cfg = KEYS[set];
            var val = localStorage.getItem(cfg.ls) || cfg.def;
            document.documentElement.setAttribute(cfg.attr, val);
            document.querySelectorAll('[data-set="' + set + '"]').forEach(function (b) {
                b.classList.toggle('active', b.getAttribute('data-val') === val);
            });
        });
    }
    document.addEventListener('click', function (e) {
        var opt = e.target.closest('[data-set][data-val]');
        if (!opt) return;
        var cfg = KEYS[opt.getAttribute('data-set')];
        if (!cfg) return;
        localStorage.setItem(cfg.ls, opt.getAttribute('data-val'));
        apply();
    });
    document.addEventListener('DOMContentLoaded', apply);
    (function registerEnhanced(attempts) {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', apply);
        } else if (attempts < 200) {
            setTimeout(function () { registerEnhanced(attempts + 1); }, 50);
        }
    })(0);
    apply();
})();
