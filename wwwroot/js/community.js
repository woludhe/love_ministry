// ============================================================
//  Community feed: Amen / Like, comment box, "View all comments", "See more", deleting posts and comments
//  Used by Views/Community/Index.cshtml and Post.cshtml (cards from _PostCard.cshtml)
// ============================================================

document.addEventListener('DOMContentLoaded', () => {
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

  // True once the item is gone; 404 means someone else already removed it
  const deleteItem = async (url, id) => {
    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
        body: JSON.stringify(Number(id))
      });
      // A refused or expired login redirects to a page that loads fine, so that isn't success
      return !res.redirected && (res.ok || res.status === 404);
    } catch {
      return false;
    }
  };

  document.addEventListener('click', async e => {
    // Delete a post: off the feed in place, or back to the feed from the post page
    const delPost = e.target.closest('.js-delete-post');
    if (delPost) {
      if (!confirm('Delete this post? Its comments and photos are deleted too. This cannot be undone.')) return;
      delPost.disabled = true;
      if (await deleteItem('/Community/DeletePost', delPost.dataset.id)) {
        if (document.querySelector('.js-feed')) delPost.closest('.fb-post').remove();
        else location.href = '/Community';
      } else {
        delPost.disabled = false;
      }
      return;
    }

    // Delete a comment, then bring the card's comment counts up to date
    const delReply = e.target.closest('.js-delete-reply');
    if (delReply) {
      if (!confirm('Delete this comment? This cannot be undone.')) return;
      delReply.disabled = true;
      if (await deleteItem('/Community/DeleteReply', delReply.dataset.id)) {
        const card = delReply.closest('.fb-post');
        delReply.closest('.fb-comment').remove();
        const left = card.querySelectorAll('.fb-comment').length;
        card.querySelectorAll('.js-comment-count').forEach(c => c.textContent = left);
        card.querySelectorAll('.js-comment-word').forEach(w => w.textContent = left === 1 ? 'comment' : 'comments');
        if (!card.querySelector('.fb-comment.is-hidden')) card.querySelector('.js-view-all')?.remove();
      } else {
        delReply.disabled = false;
      }
      return;
    }

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

  // Feed: the next 10 posts load as the "Load more posts" link comes near the screen
  // (without scripts it is a plain link to the next batch)
  const feed = document.querySelector('.js-feed');
  if (feed && 'IntersectionObserver' in window) {
    let busy = false;
    const loadMore = async link => {
      if (busy) return;
      busy = true;
      link.classList.add('is-busy');
      try {
        const res = await fetch(link.href, { headers: { 'X-Requested-With': 'fetch' } });
        if (!res.ok) throw new Error(res.status);
        const batch = document.createElement('div');
        batch.innerHTML = await res.text();
        bindComments(batch);
        link.replaceWith(...batch.childNodes);
        watch();
      } catch {
        link.classList.remove('is-busy');   // the link still works as a normal click
      }
      busy = false;
    };
    const observer = new IntersectionObserver(entries => {
      entries.forEach(en => { if (en.isIntersecting) { observer.unobserve(en.target); loadMore(en.target); } });
    }, { rootMargin: '600px 0px' });
    const watch = () => feed.querySelectorAll('.js-load-more:not(.is-busy)').forEach(l => observer.observe(l));
    watch();
  }

  bindComments(document);
});

// Comment boxes grow with their text; Enter posts, Shift+Enter makes a new line
function bindComments(root) {
  root.querySelectorAll('.fb-comment-form textarea').forEach(t => {
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
}
