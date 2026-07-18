// ==UserScript==
// @name         PoE2 TradeBot Bridge
// @namespace    http://tampermonkey.net/
// @version      0.9
// @match        https://www.pathofexile.com/*
// @run-at       document-start
// @grant        none
// ==/UserScript==

(function () {
    'use strict';

    var NativeWS = window.WebSocket;
    var botWs = null;

    // Auto-click "Teleport anyway?" when GGG's trade site shows an "in demand" dialog.
    // The dialog appears ~0ms after a live search hit, while our bot chain takes ~500ms.
    // Clicking the button makes the site send the whisper (including its own confirmation
    // flow for the gold cost), so we don't need to wait for the bot chain at all.
    function setupInDemandAutoConfirm() {
        var observer = new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var nodes = mutations[i].addedNodes;
                for (var j = 0; j < nodes.length; j++) {
                    var node = nodes[j];
                    if (node.nodeType !== 1) continue;
                    var buttons = node.querySelectorAll('button');
                    for (var k = 0; k < buttons.length; k++) {
                        if (buttons[k].textContent.includes('Teleport anyway')) {
                            console.log('[TradeBot] in demand dialog detected, auto-clicking Teleport anyway');
                            buttons[k].click();
                            break;
                        }
                    }
                }
            }
        });
        observer.observe(document.documentElement, { childList: true, subtree: true });
    }

    function connect() {
        if (botWs && botWs.readyState < 2) return;
        try { botWs = new NativeWS('ws://127.0.0.1:8765'); } catch (e) { setTimeout(connect, 3000); return; }
        botWs.onopen = function () { console.log('[TradeBot] connected'); };
        botWs.onclose = function () { setTimeout(connect, 3000); };
        botWs.onerror = function () {};
        botWs.onmessage = function (e) {
            try {
                var cmd = JSON.parse(e.data);
                if (cmd.type === 'whisper') doWhisper(cmd.token, cmd.double === true);
            } catch (_) {}
        };
    }

    // Calls /api/trade2/whisper and sends {type:"whisper_result", ok, status} back to TradeBot.
    // double=true (mode B): first {token}, then after 300ms {continue:true, token}.
    // double=false (mode A): single {continue:true, token}.
    function doWhisper(token, isDouble) {
        if (isDouble) {
            // Mode B: two-step flow mimicking the trade site's "in demand" confirmation sequence.
            whisperOnce(token, false, function (ok1, status1) {
                console.log('[TradeBot] whisper step1 status:', status1);
                setTimeout(function () {
                    whisperOnce(token, true, function (ok2, status2) {
                        console.log('[TradeBot] whisper step2 status:', status2);
                        sendWhisperResult(ok1 || ok2, status2);
                    });
                }, 300);
            });
        } else {
            // Mode A: single request with continue:true (original behaviour).
            whisperOnce(token, true, function (ok, status) {
                console.log('[TradeBot] whisper status:', status);
                sendWhisperResult(ok, status);
            });
        }
    }

    function whisperOnce(token, withContinue, callback) {
        var body = withContinue
            ? { 'continue': true, token: token }
            : { token: token };
        fetch('/api/trade2/whisper', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            body: JSON.stringify(body),
            credentials: 'include'
        }).then(function (r) {
            var status = r.status;
            var ok = r.ok;
            return r.json().catch(function () { return null; }).then(function (data) {
                if (data) console.log('[TradeBot] whisper body:', JSON.stringify(data));
                callback(ok, status);
            });
        }).catch(function (err) {
            console.log('[TradeBot] whisper error:', err);
            callback(false, 0);
        });
    }

    function sendWhisperResult(ok, status) {
        if (botWs && botWs.readyState === 1) {
            botWs.send(JSON.stringify({ type: 'whisper_result', ok: ok, status: status }));
        }
    }

    window.WebSocket = function (url, protocols) {
        var ws = protocols !== undefined ? new NativeWS(url, protocols) : new NativeWS(url);
        if (typeof url === 'string' && url.includes('trade2/live')) {
            ws.addEventListener('message', function (e) {
                try {
                    var d = JSON.parse(e.data);
                    if (d.result && botWs && botWs.readyState === 1) {
                        fetch('/api/trade2/fetch/' + d.result, { credentials: 'include' })
                            .then(function (r) { return r.ok ? r.json() : null; })
                            .then(function (data) {
                                if (data && botWs && botWs.readyState === 1) {
                                    botWs.send(JSON.stringify({ type: 'items', data: data }));
                                }
                            }).catch(function () {});
                    }
                } catch (_) {}
            });
        }
        return ws;
    };
    window.WebSocket.prototype = NativeWS.prototype;
    window.WebSocket.CONNECTING = 0; window.WebSocket.OPEN = 1;
    window.WebSocket.CLOSING = 2; window.WebSocket.CLOSED = 3;

    setupInDemandAutoConfirm();
    setTimeout(connect, 500);
})();
