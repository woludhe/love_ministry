// "New" tags on the member sidebar (Live Chat, Community) and their shortcuts on the phone
// "Dashboard Menu" bar: count chat messages and community posts from others as they arrive
(function () {
    const root = document.getElementById('navUnread');
    if (!root || !window.signalR) return;
    const myId = Number(root.dataset.memberId);
    const onChatPage = location.pathname.toLowerCase().startsWith('/community/chat');

    function counter(tagId, barId) {
        const tag = document.getElementById(tagId);
        const bar = document.getElementById(barId);
        let count = parseInt(tag?.textContent, 10) || 0;
        return () => {
            count++;
            const label = count > 99 ? '99+' : String(count);
            for (const el of [tag, bar]) {
                if (!el) continue;
                (el.querySelector('.toggle-chat-count') ?? el).textContent = label;
                el.hidden = false;
                el.classList.remove('pulse');
                void el.offsetWidth;   // restart the animation
                el.classList.add('pulse');
            }
        };
    }
    const newChat = counter('chatUnread', 'chatUnreadBar');
    const newPost = counter('communityUnread', 'communityUnreadBar');

    const conn = new signalR.HubConnectionBuilder().withUrl('/chatHub').withAutomaticReconnect().build();
    conn.on('ChatActivity', a => { if (a.memberId !== myId && !onChatPage) newChat(); });   // the chat page shows them itself
    conn.on('CommunityActivity', a => { if (a.memberId !== myId) newPost(); });
    conn.start().catch(() => { });
})();
