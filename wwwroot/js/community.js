// ============================================================
//  Community feed: Amen / Like, comment box, "View all comments", "See more"
//  Used by Views/Community/Index.cshtml and Post.cshtml (cards from _PostCard.cshtml)
// ============================================================

document.addEventListener('DOMContentLoaded', () => {
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

  document.addEventListener('click', async e => {
    // Amen / Like: toggle, then show the server's count
    const react = e.target.closest('.js-react');
    if (react) {
      const card = react.closest('.fb-post');
      const kind = react.dataset.kind;   // "amen" or "like"
      react.disabled = true;
      try {
        const res = await fetch(kind === 'like' ? '/Community/Like' : '/Community/Amen', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
          body: JSON.stringify(Number(react.dataset.id))
        });
        if (!res.ok) throw new Error(res.status);
        const d = await res.json();
        react.classList.toggle('is-on', d.on);
        react.setAttribute('aria-pressed', d.on);
        card.querySelector(`.js-${kind}-count`).textContent = d.count;
        card.querySelector(`.js-${kind}-summary`).hidden = d.count === 0;
        const word = card.querySelector(`.js-${kind}-word`);
        if (word) word.textContent = d.count === 1 ? 'Like' : 'Likes';
      } catch { /* leave the button as it was */ }
      react.disabled = false;
      return;
    }

    // Comment: jump to this post's comment box
    const comment = e.target.closest('.js-comment');
    if (comment) {
      document.getElementById(comment.dataset.target)?.focus();
      return;
    }

    // View all comments: reveal the older ones in place
    const viewAll = e.target.closest('.js-view-all');
    if (viewAll) {
      viewAll.closest('.fb-comments').querySelectorAll('.fb-comment.is-hidden')
        .forEach(c => c.classList.remove('is-hidden'));
      viewAll.remove();
      return;
    }

    // See more: unclamp a long post
    const more = e.target.closest('.js-see-more');
    if (more) {
      more.previousElementSibling.classList.remove('is-clamped');
      more.remove();
    }
  });

  // Post photos: click opens a full-screen viewer; arrows / ← → step through that post's photos, Esc closes
  const viewer = document.createElement('div');
  viewer.className = 'fb-viewer';
  viewer.hidden = true;
  viewer.innerHTML = `
    <button type="button" class="fb-viewer-close" aria-label="Close"><i class="fas fa-times"></i></button>
    <button type="button" class="fb-viewer-prev" aria-label="Previous photo"><i class="fas fa-chevron-left"></i></button>
    <img alt="" />
    <button type="button" class="fb-viewer-next" aria-label="Next photo"><i class="fas fa-chevron-right"></i></button>
    <div class="fb-viewer-count"></div>`;
  document.body.appendChild(viewer);
  let photos = [], at = 0;

  const show = i => {
    at = (i + photos.length) % photos.length;
    viewer.querySelector('img').src = photos[at];
    viewer.querySelector('.fb-viewer-count').textContent = photos.length > 1 ? `${at + 1} / ${photos.length}` : '';
    viewer.querySelectorAll('.fb-viewer-prev, .fb-viewer-next').forEach(b => b.hidden = photos.length < 2);
  };
  const close = () => { viewer.hidden = true; document.body.style.overflow = ''; };

  document.addEventListener('click', e => {
    const photo = e.target.closest('.js-photo');
    if (!photo) return;
    photos = [...photo.closest('.fb-photos').querySelectorAll('.js-photo')].map(b => b.dataset.src);
    show(photos.indexOf(photo.dataset.src));
    viewer.hidden = false;
    document.body.style.overflow = 'hidden';
  });
  viewer.addEventListener('click', e => {
    if (e.target.closest('.fb-viewer-prev')) show(at - 1);
    else if (e.target.closest('.fb-viewer-next')) show(at + 1);
    else if (e.target.tagName !== 'IMG') close();
  });
  document.addEventListener('keydown', e => {
    if (viewer.hidden) return;
    if (e.key === 'Escape') close();
    if (e.key === 'ArrowLeft' && photos.length > 1) show(at - 1);
    if (e.key === 'ArrowRight' && photos.length > 1) show(at + 1);
  });

  // Comment boxes grow with their text; Enter posts, Shift+Enter makes a new line
  document.querySelectorAll('.fb-comment-form textarea').forEach(t => {
    const grow = () => { t.style.height = 'auto'; t.style.height = t.scrollHeight + 'px'; };
    t.addEventListener('input', grow);
    t.addEventListener('keydown', e => {
      if (e.key === 'Enter' && !e.shiftKey && t.value.trim()) {
        e.preventDefault();
        t.form.requestSubmit();
      }
    });
    t.form.addEventListener('submit', () => {
      setTimeout(() => t.form.querySelector('button[type=submit]').disabled = true);
    });
  });
});
