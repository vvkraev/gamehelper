// ==UserScript==
// @name         PoE2 Trade Auto-Capture
// @namespace    gamehelper
// @version      1.2
// @description  Перехватывает /api/trade2/fetch и отправляет на localhost:7123
// @match        https://www.pathofexile.com/trade2*
// @match        https://pathofexile.com/trade2*
// @run-at       document-start
// @grant        unsafeWindow
// ==/UserScript==

(function () {
    'use strict';

    var win = (typeof unsafeWindow !== 'undefined') ? unsafeWindow : window;
    var ENDPOINT = 'http://localhost:7123/trade-import';
    var origFetch = win.fetch;

    win.fetch = function () {
        var args = arguments;
        return origFetch.apply(win, args).then(function (res) {
            var url = typeof args[0] === 'string' ? args[0] : (args[0] && args[0].url) || '';
            if (url.indexOf('/api/trade2/fetch') >= 0) {
                res.clone().text().then(function (body) {
                    origFetch.call(win, ENDPOINT, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: body,
                    }).then(function (r) {
                        return r.json();
                    }).then(function (data) {
                        console.log('[TradeCapture] OK: ' + data.status);
                    }).catch(function () {
                        console.warn('[TradeCapture] порт 7123 недоступен');
                    });
                });
            }
            return res;
        });
    };

    console.log('[TradeCapture] Активен (unsafeWindow) → localhost:7123');
})();
