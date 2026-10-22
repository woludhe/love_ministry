// ============================================================
//  Love Fellowship Ministries - site.js
// ============================================================

document.addEventListener('DOMContentLoaded', () => {

  // ── Mobile nav toggle ──
  const toggle   = document.getElementById('navToggle');
  const links    = document.getElementById('navLinks');
  const backdrop = document.getElementById('navBackdrop');
  if (toggle && links) {
    const setMenu = open => {
      links.classList.toggle('open', open);
      backdrop?.classList.toggle('open', open);
      document.body.classList.toggle('nav-open', open);
      toggle.setAttribute('aria-expanded', open);
      toggle.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
      toggle.innerHTML = `<i class="fas ${open ? 'fa-xmark' : 'fa-bars'}"></i>`;
      // Only one panel at a time: opening the menu closes notifications
      if (open) document.getElementById('notifDropdown')?.classList.remove('open');
    };
    toggle.addEventListener('click', e => {
      e.stopPropagation();
      setMenu(!links.classList.contains('open'));
    });
    backdrop?.addEventListener('click', () => setMenu(false));
    links.querySelectorAll('a').forEach(a => a.addEventListener('click', () => setMenu(false)));
    document.addEventListener('click', e => {
      if (links.classList.contains('open') && !toggle.contains(e.target) && !links.contains(e.target))
        setMenu(false);
    });
    document.addEventListener('keydown', e => { if (e.key === 'Escape') setMenu(false); });
    // Opening notifications closes the menu
    document.getElementById('notifBell')?.addEventListener('click', () => {
      if (links.classList.contains('open')) setMenu(false);
    });
    // Back to desktop width: reset
    window.matchMedia('(min-width: 769px)').addEventListener('change', e => { if (e.matches) setMenu(false); });
  }

  // ── Dashboard sidebar drawer (mobile) ──
  const sideToggle   = document.getElementById('sidebarToggle');
  const sidebar      = document.getElementById('dashSidebar');
  const sideBackdrop = document.getElementById('sidebarBackdrop');
  if (sideToggle && sidebar) {
    const setSidebar = open => {
      sidebar.classList.toggle('open', open);
      sideBackdrop?.classList.toggle('open', open);
      document.body.classList.toggle('sidebar-open', open);
      sideToggle.setAttribute('aria-expanded', open);
    };
    sideToggle.addEventListener('click', () => setSidebar(!sidebar.classList.contains('open')));
    sideBackdrop?.addEventListener('click', () => setSidebar(false));
    sidebar.querySelector('.sidebar-close')?.addEventListener('click', () => setSidebar(false));
    sidebar.querySelectorAll('.sidebar-nav a').forEach(a => a.addEventListener('click', () => setSidebar(false)));
    document.addEventListener('keydown', e => { if (e.key === 'Escape') setSidebar(false); });
  }

  // ── Label table cells so tables can render as cards on mobile ──
  document.querySelectorAll('table.data-table').forEach(table => {
    const headers = [...table.querySelectorAll('thead th')].map(th => th.textContent.trim());
    table.querySelectorAll('tbody tr, tfoot tr').forEach(tr => {
      [...tr.children].forEach((td, i) => {
        if (td.tagName === 'TD' && !td.hasAttribute('colspan') && headers[i])
          td.dataset.label = headers[i];
      });
    });
  });

  // ── Auto-dismiss alerts ──
  document.querySelectorAll('.alert').forEach(el => {
    setTimeout(() => { el.style.transition = 'opacity .5s'; el.style.opacity = '0'; setTimeout(() => el.remove(), 500); }, 4500);
  });

  // ── Payment method card highlight ──
  document.querySelectorAll('.method-card').forEach(card => {
    const radio = card.querySelector('input[type=radio]');
    if (radio) {
      radio.addEventListener('change', () => {
        document.querySelectorAll('.method-card').forEach(c => c.classList.remove('selected'));
        if (radio.checked) card.classList.add('selected');
      });
    }
  });

  // ── Type pill selection feedback ──
  document.querySelectorAll('.pill-label').forEach(label => {
    label.addEventListener('change', () => {
      document.querySelectorAll('.pill-label span').forEach(s => s.style.fontWeight = '');
    });
  });

  // ── Confirm dangerous actions ──
  document.querySelectorAll('[data-confirm]').forEach(el => {
    el.addEventListener('click', e => {
      if (!confirm(el.dataset.confirm)) e.preventDefault();
    });
  });

  // ── Smooth scroll for anchor links ──
  document.querySelectorAll('a[href^="#"]').forEach(anchor => {
    anchor.addEventListener('click', e => {
      const target = document.querySelector(anchor.getAttribute('href'));
      if (target) { e.preventDefault(); target.scrollIntoView({ behavior: 'smooth', block: 'start' }); }
    });
  });

  // ── Live stream countdown (if data-service-time attr set) ──
  const countdown = document.getElementById('serviceCountdown');
  if (countdown && countdown.dataset.serviceTime) {
    const target = new Date(countdown.dataset.serviceTime);
    const update = () => {
      const diff = target - new Date();
      if (diff <= 0) { countdown.textContent = 'Service is now live!'; return; }
      const h = Math.floor(diff / 3600000);
      const m = Math.floor((diff % 3600000) / 60000);
      const s = Math.floor((diff % 60000) / 1000);
      countdown.textContent = `${h}h ${m}m ${s}s`;
    };
    update();
    setInterval(update, 1000);
  }

  // ── Form password strength indicator ──
  const pwdInput = document.querySelector('input[type=password][name=Password]');
  if (pwdInput) {
    const bar = document.createElement('div');
    bar.style.cssText = 'height:4px;border-radius:4px;margin-top:4px;background:#e5e7eb;transition:width .3s';
    const fill = document.createElement('div');
    fill.style.cssText = 'height:100%;border-radius:4px;transition:width .3s,background .3s;width:0';
    bar.appendChild(fill);
    pwdInput.parentNode.insertBefore(bar, pwdInput.nextSibling);

    pwdInput.addEventListener('input', () => {
      const v = pwdInput.value;
      let strength = 0;
      if (v.length >= 8) strength++;
      if (/[A-Z]/.test(v)) strength++;
      if (/[0-9]/.test(v)) strength++;
      if (/[^A-Za-z0-9]/.test(v)) strength++;
      const pcts = ['0%','30%','55%','80%','100%'];
      const colors = ['','#ef4444','#f59e0b','#10b981','#6366f1'];
      fill.style.width  = pcts[strength];
      fill.style.background = colors[strength] || '#e5e7eb';
    });
  }

  // ── Image preview helper (re-usable) ──
  window.previewImage = (input, imgId) => {
    if (input.files && input.files[0]) {
      const reader = new FileReader();
      reader.onload = e => { const img = document.getElementById(imgId); if (img) img.src = e.target.result; };
      reader.readAsDataURL(input.files[0]);
    }
  };

  // ── Toast helper (reusable from inline JS) ──
  window.showToast = (msg, type = 'success') => {
    const toast = document.createElement('div');
    toast.className = `alert alert-${type}`;
    toast.style.cssText = 'position:fixed;bottom:1.5rem;right:1.5rem;z-index:9999;border-radius:10px;box-shadow:0 4px 12px rgba(0,0,0,.15);max-width:340px;animation:slideIn .3s ease';
    toast.innerHTML = `<i class="fas fa-${type==='success'?'check-circle':'exclamation-circle'}"></i> ${msg}`;
    document.body.appendChild(toast);
    setTimeout(() => { toast.style.opacity = '0'; toast.style.transition = 'opacity .5s'; setTimeout(() => toast.remove(), 500); }, 3500);
  };
});
