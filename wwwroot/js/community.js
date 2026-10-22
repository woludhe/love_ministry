// ============================================================
//  Community feed: Amen, comment box, "View all comments", "See more"
//  Used by Views/Community/Index.cshtml and Post.cshtml (cards from _PostCard.cshtml)
// ============================================================

document.addEventListener('DOMContentLoaded', () => {
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

  document.addEventListener('click', async e => {
    // Amen: toggle, then show the server's count
    const amen = e.target.closest('.js-amen');
    if (amen) {
      const card = amen.closest('.fb-post');
      amen.disabled = true;
      try {
        const res = await fetch('/Community/Amen', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
          body: JSON.stringify(Number(amen.dataset.id))
        });
        if (!res.ok) throw new Error(res.status);
        const d = await res.json();
        amen.classList.toggle('is-on', d.amened);
        amen.setAttribute('aria-pressed', d.amened);
        card.querySelector('.js-amen-count').textContent = d.count;
        card.querySelector('.js-amen-summary').hidden = d.count === 0;
      } catch { /* leave the button as it was */ }
      amen.disabled = false;
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
