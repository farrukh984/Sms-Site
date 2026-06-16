/**
 * ai-chat.js — SMS SITE AI Assistant Widget
 * Uses Google Gemini API (free tier) to provide intelligent guidance
 * about the SMS Site platform features
 */

(function () {
    'use strict';

    // ─── Configuration ───────────────────────────────────────────────────────────
    // API call goes through our ASP.NET backend proxy (key is stored server-side in appsettings.json)
    const GEMINI_URL = '/Home/AiChatProxy';

    // System prompt: SMS Site ke baare mein AI ko train karta hai
    const SYSTEM_PROMPT = `You are an AI assistant for SMS SITE — a modern, feature-rich messaging platform similar to WhatsApp. You help users understand and navigate the platform.

SMS SITE Features you can help with:
- **Chats (/Chat):** Send and receive messages. Direct 1-on-1 and group chats supported.
- **Status (/Status):** Share temporary status updates that disappear after 24 hours.
- **Channels (/Channel):** Follow channels to receive broadcasts from admins.
- **Calls (/Call):** Make voice and video calls to contacts.
- **Communities (/Community):** Join groups of channels and communities.
- **Services (/Services):** Explore extra platform services and tools.
- **Settings (/Settings):** Update profile, privacy settings, security, and notifications.
- **Contacts:** Add contacts by phone number. You can only send 5 messages until the other person adds you back.
- **Themes:** Toggle between Dark and Light mode in Settings.
- **End-to-end encryption:** All chats are secured.
- **Media sharing:** Share images, videos, documents in chats.
- **Group chats:** Create groups, add members, set group photo.
- **Reactions:** React to messages with emojis.
- **Message deletion:** Delete your own messages.
- **Reply to messages:** Reply to specific messages in a chat.

Rules:
- Always be helpful, concise, and friendly.
- If a user asks about a feature, provide the navigation path (e.g., "Go to /Settings > Privacy").
- Keep responses short — under 3 sentences when possible.
- If asked something unrelated to SMS SITE, gently redirect to platform help.
- Respond in the same language the user writes in (Urdu, English, etc.).
- For Urdu, use simple Roman Urdu if the user writes Roman Urdu.`;

    // ─── State ───────────────────────────────────────────────────────────────────
    let isOpen       = false;
    let isThinking   = false;
    let history      = []; // Gemini multi-turn conversation history
    let hasUserMessage = false;

    // ─── DOM Elements ─────────────────────────────────────────────────────────────
    let trigger, panel, feed, inputBox, sendBtn;

    // ─── Helpers ─────────────────────────────────────────────────────────────────
    function formatTime() {
        const now = new Date();
        return now.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: true });
    }

    function escapeHtml(text) {
        const d = document.createElement('div');
        d.textContent = text;
        return d.innerHTML;
    }

    function formatMarkdown(text) {
        // Simple markdown: **bold**, *italic*, `code`, newlines
        return escapeHtml(text)
            .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
            .replace(/\*(.+?)\*/g, '<em>$1</em>')
            .replace(/`(.+?)`/g, '<code style="background:rgba(255,255,255,0.1);padding:1px 5px;border-radius:4px;font-size:12px;">$1</code>')
            .replace(/\n/g, '<br>');
    }

    // ─── Append Message ──────────────────────────────────────────────────────────
    function appendMessage(role, text, isError) {
        // Remove welcome state if first real message
        const welcome = feed.querySelector('.ai-welcome-state');
        if (welcome) welcome.remove();

        const wrapper = document.createElement('div');
        wrapper.className = `ai-msg ${role === 'user' ? 'user' : 'bot'}`;

        if (role === 'model') {
            wrapper.innerHTML = `
                <div class="ai-msg-avatar">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M12 2a10 10 0 1 1 0 20A10 10 0 0 1 12 2z"/>
                        <path d="M8 12h.01M12 12h.01M16 12h.01"/>
                    </svg>
                </div>
                <div>
                    <div class="ai-msg-bubble ${isError ? 'ai-error-toast' : ''}">${isError ? text : formatMarkdown(text)}</div>
                    <div class="ai-msg-time">${formatTime()}</div>
                </div>`;
        } else {
            wrapper.innerHTML = `
                <div>
                    <div class="ai-msg-bubble">${escapeHtml(text)}</div>
                    <div class="ai-msg-time">${formatTime()}</div>
                </div>`;
        }

        feed.appendChild(wrapper);
        feed.scrollTop = feed.scrollHeight;
        return wrapper;
    }

    // ─── Typing Indicator ────────────────────────────────────────────────────────
    function showTyping() {
        const wrapper = document.createElement('div');
        wrapper.className = 'ai-msg bot';
        wrapper.id = 'ai-typing-wrapper';
        wrapper.innerHTML = `
            <div class="ai-msg-avatar">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M12 2a10 10 0 1 1 0 20A10 10 0 0 1 12 2z"/>
                    <path d="M8 12h.01M12 12h.01M16 12h.01"/>
                </svg>
            </div>
            <div class="ai-typing-indicator">
                <div class="ai-typing-dot"></div>
                <div class="ai-typing-dot"></div>
                <div class="ai-typing-dot"></div>
            </div>`;
        feed.appendChild(wrapper);
        feed.scrollTop = feed.scrollHeight;
    }

    function hideTyping() {
        const el = document.getElementById('ai-typing-wrapper');
        if (el) el.remove();
    }

    // ─── Send Message ────────────────────────────────────────────────────────────
    async function sendMessage(text) {
        if (!text.trim() || isThinking) return;

        text = text.trim();
        isThinking = true;
        sendBtn.disabled = true;
        inputBox.value   = '';
        inputBox.style.height = 'auto';

        // Remove welcome state
        const welcome = feed.querySelector('.ai-welcome-state');
        if (welcome) welcome.remove();

        // Show user message
        appendMessage('user', text);
        hasUserMessage = true;

        // Add to history
        history.push({ role: 'user', parts: [{ text }] });

        // Show typing
        showTyping();

        try {
            // Gemini API call
            const payload = {
                system_instruction: {
                    parts: [{ text: SYSTEM_PROMPT }]
                },
                contents: history,
                generationConfig: {
                    temperature: 0.7,
                    maxOutputTokens: 600,
                    topP: 0.9
                }
            };

            let resp;
            try {
                // Simple JSON — server proxy handles Google auth
                const headers = { 'Content-Type': 'application/json' };

                resp = await fetch(GEMINI_URL, {
                    method:  'POST',
                    headers: headers,
                    body:    JSON.stringify(payload)
                });
            } catch (networkErr) {
                throw new Error('NETWORK: ' + networkErr.message);
            }

            const rawText = await resp.text();
            let data;
            try { data = JSON.parse(rawText); } catch { data = {}; }

            if (!resp.ok) {
                const apiMsg = data?.error?.message || `HTTP ${resp.status}`;
                console.error('[AI Chat] API Error:', resp.status, apiMsg, rawText);
                throw new Error('API: ' + apiMsg);
            }

            const replyText = data?.candidates?.[0]?.content?.parts?.[0]?.text;

            if (!replyText) {
                console.error('[AI Chat] Empty response:', rawText);
                throw new Error('Empty response from AI');
            }

            // Add to history for multi-turn
            history.push({ role: 'model', parts: [{ text: replyText }] });

            hideTyping();
            appendMessage('model', replyText);

        } catch (err) {
            hideTyping();
            console.error('[AI Chat] Final error:', err.message);

            let errorMsg;
            const msg = err.message.toLowerCase();

            if (msg.includes('api key') || msg.includes('api_key') || msg.includes('invalid key')) {
                errorMsg = '❌ API Key galat hai. ai-chat.js mein sahi key lagaein.';
            } else if (msg.includes('quota') || msg.includes('429') || msg.includes('resource exhausted')) {
                errorMsg = '⏳ Free daily limit khatam ho gayi. Quota midnight UTC (5am PKT) pe reset hoga — kal subah dobara try karein, ya AI Studio mein billing enable karein.';
            } else if (msg.includes('network') || msg.includes('failed to fetch')) {
                errorMsg = '📡 Network issue. Internet connection check karein.';
            } else if (msg.includes('403') || msg.includes('permission')) {
                errorMsg = '🔒 API Key ko Gemini access nahi mila. AI Studio mein check karein.';
            } else if (msg.includes('404') || msg.includes('not found')) {
                errorMsg = '🔄 AI model update ho raha hai. Thodi der mein try karein.';
            } else {
                errorMsg = '⚠️ Error: ' + err.message.slice(0, 80);
            }

            appendMessage('model', errorMsg, true);
            // Remove last user message from history on error
            history.pop();
        }

        isThinking   = false;
        sendBtn.disabled = false;
        inputBox.focus();
    }

    // ─── Panel Open / Close ──────────────────────────────────────────────────────
    function openPanel() {
        isOpen = true;
        panel.classList.add('visible');
        trigger.classList.add('open');

        // Remove notification dot
        const dot = trigger.querySelector('.ai-notif-dot');
        if (dot) dot.style.display = 'none';

        setTimeout(() => inputBox && inputBox.focus(), 200);
    }

    function closePanel() {
        isOpen = false;
        panel.classList.remove('visible');
        trigger.classList.remove('open');
    }

    function togglePanel() {
        isOpen ? closePanel() : openPanel();
    }

    // ─── Clear Chat ──────────────────────────────────────────────────────────────
    function clearChat() {
        history        = [];
        hasUserMessage = false;
        feed.innerHTML = buildWelcomeState();
        inputBox.focus();
    }

    // ─── Build HTML ──────────────────────────────────────────────────────────────
    function buildWelcomeState() {
        return `
        <div class="ai-welcome-state">
            <div class="ai-welcome-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
                    <path d="M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z"/>
                    <circle cx="9" cy="10" r="1" fill="currentColor" stroke="none"/>
                    <circle cx="12" cy="10" r="1" fill="currentColor" stroke="none"/>
                    <circle cx="15" cy="10" r="1" fill="currentColor" stroke="none"/>
                </svg>
            </div>
            <p class="ai-welcome-title">SMS SITE Assistant</p>
            <p class="ai-welcome-sub">Koi bhi sawal poochein — features, settings, ya help ke baare mein.</p>
        </div>`;
    }

    const SUGGESTIONS = [
        'How to send a message?',
        'Group chat banana',
        'Privacy settings',
        'Status kaise lagaein?',
        'Contact add karo',
    ];

    function buildWidget() {
        // ─ Floating Trigger Button ─
        trigger = document.createElement('button');
        trigger.id = 'ai-chat-trigger';
        trigger.title = 'SMS SITE AI Assistant';
        trigger.setAttribute('aria-label', 'Open AI Assistant');
        trigger.innerHTML = `
            <svg class="icon-ai" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z"/>
                <path d="M8 10h.01M12 10h.01M16 10h.01" stroke-width="2.5"/>
            </svg>
            <svg class="icon-close" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round">
                <line x1="18" y1="6" x2="6" y2="18"/>
                <line x1="6" y1="6" x2="18" y2="18"/>
            </svg>
            <span class="ai-notif-dot"></span>`;

        // ─ Panel ─
        panel = document.createElement('div');
        panel.id = 'ai-chat-panel';
        panel.setAttribute('role', 'dialog');
        panel.setAttribute('aria-label', 'AI Chat Assistant');

        // Suggestion chips HTML
        const chipsHtml = SUGGESTIONS.map(s =>
            `<button class="ai-suggestion-chip" data-msg="${escapeHtml(s)}">${escapeHtml(s)}</button>`
        ).join('');

        panel.innerHTML = `
            <div class="ai-panel-header">
                <div class="ai-avatar-ring">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M12 2a10 10 0 1 1 0 20A10 10 0 0 1 12 2z"/>
                        <path d="M8 12h.01M12 12h.01M16 12h.01" stroke-width="2.5"/>
                    </svg>
                </div>
                <div class="ai-header-info">
                    <div class="ai-header-name">SMS SITE AI</div>
                    <div class="ai-header-status">Online &amp; Ready</div>
                </div>
                <div class="ai-header-actions">
                    <button class="ai-header-btn" id="ai-clear-btn" title="Clear chat" aria-label="Clear chat">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
                            <polyline points="1 4 1 10 7 10"/>
                            <path d="M3.51 15a9 9 0 1 0 .49-3.7"/>
                        </svg>
                    </button>
                    <button class="ai-header-btn" id="ai-close-btn" title="Close" aria-label="Close chat">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round">
                            <line x1="18" y1="6" x2="6" y2="18"/>
                            <line x1="6" y1="6" x2="18" y2="18"/>
                        </svg>
                    </button>
                </div>
            </div>

            <div class="ai-suggestions" id="ai-suggestions-bar">
                ${chipsHtml}
            </div>

            <div class="ai-message-feed" id="ai-message-feed">
                ${buildWelcomeState()}
            </div>

            <div class="ai-input-bar">
                <div class="ai-input-wrap">
                    <textarea
                        class="ai-input-box"
                        id="ai-input-box"
                        placeholder="Kuch bhi poochhein..."
                        rows="1"
                        maxlength="1000"
                        aria-label="Type a message"
                    ></textarea>
                </div>
                <button class="ai-send-btn" id="ai-send-btn" disabled title="Send" aria-label="Send message">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                        <line x1="22" y1="2" x2="11" y2="13"/>
                        <polygon points="22 2 15 22 11 13 2 9 22 2"/>
                    </svg>
                </button>
            </div>
            <div class="ai-input-hint">Enter = Send &nbsp;·&nbsp; Shift+Enter = New line</div>`;

        document.body.appendChild(trigger);
        document.body.appendChild(panel);

        // Cache sub-elements
        feed     = panel.querySelector('#ai-message-feed');
        inputBox = panel.querySelector('#ai-input-box');
        sendBtn  = panel.querySelector('#ai-send-btn');

        // ─ Events ─
        trigger.addEventListener('click', togglePanel);

        panel.querySelector('#ai-close-btn').addEventListener('click', closePanel);
        panel.querySelector('#ai-clear-btn').addEventListener('click', clearChat);

        // Textarea auto-resize + enable/disable send
        inputBox.addEventListener('input', function () {
            this.style.height = 'auto';
            this.style.height = Math.min(this.scrollHeight, 90) + 'px';
            sendBtn.disabled = !this.value.trim();
        });

        // Keyboard shortcuts
        inputBox.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                if (!sendBtn.disabled) sendMessage(this.value);
            }
        });

        sendBtn.addEventListener('click', () => sendMessage(inputBox.value));

        // Suggestion chips
        panel.querySelector('#ai-suggestions-bar').addEventListener('click', function (e) {
            const chip = e.target.closest('.ai-suggestion-chip');
            if (chip) {
                sendMessage(chip.dataset.msg);
                // Hide suggestions after first pick
                this.style.display = 'none';
            }
        });

        // Close on backdrop click (outside panel)
        document.addEventListener('click', function (e) {
            if (isOpen && !panel.contains(e.target) && !trigger.contains(e.target)) {
                closePanel();
            }
        });

        // Keyboard: Escape closes panel
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && isOpen) closePanel();
        });
    }

    // ─── Init ────────────────────────────────────────────────────────────────────
    function init() {
        // Inject CSS if not already loaded
        if (!document.querySelector('link[href*="ai-chat.css"]')) {
            const link = document.createElement('link');
            link.rel  = 'stylesheet';
            link.href = '/css/ai-chat.css';
            document.head.appendChild(link);
        }

        buildWidget();

        // Greeting delay — show a subtle pulse after 2s to attract attention
        setTimeout(() => {
            const dot = trigger.querySelector('.ai-notif-dot');
            if (dot) dot.style.display = 'block';
        }, 2000);
    }

    // Run after DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
