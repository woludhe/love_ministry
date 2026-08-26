let notifOpen = false;

function toggleNotifDropdown() {
    const dropdown = document.getElementById('notifDropdown');
    notifOpen = !notifOpen;
    dropdown.classList.toggle('open', notifOpen);
    if (notifOpen) loadNotifications();
}

// Close dropdown when clicking outside
document.addEventListener('click', e => {
    if (notifOpen && !document.getElementById('notifBell').contains(e.target)) {
        document.getElementById('notifDropdown').classList.remove('open');
        notifOpen = false;
    }
});

async function loadNotifications() {
    const list = document.getElementById('notifList');
    list.innerHTML = '<div class="notif-loading"><i class="fas fa-spinner fa-spin"></i></div>';
    try {
        const res = await fetch('/Notifications/Recent');
        const data = await res.json();
        renderNotifications(data);
    } catch {
        list.innerHTML = '<div class="notif-empty"><i class="fas fa-exclamation-circle"></i> Could not load notifications</div>';
    }
}

function renderNotifications({ count, items }) {
    // Update badge
    const badge = document.getElementById('notifCount');
    if (count > 0) {
        badge.textContent = count > 99 ? '99+' : count;
        badge.style.display = 'flex';
    } else {
        badge.style.display = 'none';
    }

    const list = document.getElementById('notifList');
    if (!items || items.length === 0) {
        list.innerHTML = '<div class="notif-empty"><i class="fas fa-bell-slash"></i> No notifications yet</div>';
        return;
    }

    list.innerHTML = items.map(n => `
                    <a class="notif-drop-item ${n.isRead ? '' : 'unread'}"
                       href="/Notifications/MarkRead/${n.id}">
                        <div class="notif-drop-icon"><i class="${getNotifIcon(n.title)}"></i></div>
                        <div class="notif-drop-body">
                            <strong>${escHtml(n.title)}</strong>
                            <p>${escHtml(n.message)}</p>
                            <small>${n.createdAt}</small>
                        </div>
                        ${!n.isRead ? '<div class="notif-unread-dot"></div>' : ''}
                    </a>`).join('');
}

async function markAllRead() {
    await fetch('/Notifications/MarkAllRead', { method: 'POST' });
    document.getElementById('notifCount').style.display = 'none';
    document.querySelectorAll('.notif-drop-item.unread').forEach(el => el.classList.remove('unread'));
    document.querySelectorAll('.notif-unread-dot').forEach(el => el.remove());
}

function getNotifIcon(title) {
    const t = (title || '').toLowerCase();
    if (t.includes('prayer')) return 'fas fa-hands-praying';
    if (t.includes('giving') || t.includes('tithe')) return 'fas fa-hand-holding-heart';
    if (t.includes('event')) return 'fas fa-calendar';
    if (t.includes('sermon') || t.includes('live')) return 'fas fa-video';
    if (t.includes('member') || t.includes('welcome')) return 'fas fa-user-plus';
    if (t.includes('meeting')) return 'fas fa-video';
    return 'fas fa-bell';
}

function escHtml(s) { const d = document.createElement('div'); d.textContent = s ?? ''; return d.innerHTML; }

// Poll for new notifications every 60 seconds
async function pollNotifCount() {
    try {
        const res = await fetch('/Notifications/Recent');
        const data = await res.json();
        const badge = document.getElementById('notifCount');
        if (data.count > 0) {
            badge.textContent = data.count > 99 ? '99+' : data.count;
            badge.style.display = 'flex';
        } else {
            badge.style.display = 'none';
        }
    } catch { }
}

// Load count on page load and poll every 60s
pollNotifCount();
setInterval(pollNotifCount, 60000);