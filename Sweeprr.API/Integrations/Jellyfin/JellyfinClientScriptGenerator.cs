using System.Text.Json;

namespace Sweeprr.API.Integrations.Jellyfin;

/// <summary>
/// Generates the JavaScript injected into Jellyfin's web UI (Story 10.5) via
/// Dashboard → General → Custom JavaScript. The script polls
/// <c>/api/public/media/{id}/status</c> for the item on the current detail page
/// and renders a "Leaving Soon" banner with a link to the extension-request page.
/// </summary>
public static class JellyfinClientScriptGenerator
{
    public static string Generate(string sweeprrBaseUrl)
    {
        var baseUrlJson = JsonSerializer.Serialize(sweeprrBaseUrl.TrimEnd('/'));

        return $$"""
            (function () {
              const SWEEPRR_BASE = {{baseUrlJson}};

              let lastItemId = null;

              async function checkItem(itemId) {
                try {
                  const res = await fetch(`${SWEEPRR_BASE}/api/public/media/${itemId}/status`);
                  if (!res.ok) return null;
                  return await res.json();
                } catch {
                  return null;
                }
              }

              function injectBanner(itemId, status) {
                const existing = document.getElementById('sweeprr-banner');
                if (existing) existing.remove();

                if (!status || !status.isQueued) return;

                const banner = document.createElement('div');
                banner.id = 'sweeprr-banner';
                banner.style.cssText = `
                  background: linear-gradient(135deg, rgba(180,30,30,0.9), rgba(120,10,10,0.95));
                  border-left: 3px solid #ff4444;
                  border-radius: 6px;
                  color: white;
                  padding: 10px 14px;
                  margin: 8px 0;
                  font-family: sans-serif;
                  font-size: 13px;
                  display: flex;
                  align-items: center;
                  gap: 10px;
                `;

                const days = status.daysRemaining;
                const daysLabel = (days === null || days === undefined)
                  ? 'soon'
                  : `${days} day${days === 1 ? '' : 's'}`;

                banner.innerHTML = `
                  <span>⚠</span>
                  <span>Leaving Soon — ${daysLabel} remaining</span>
                  <a href="${SWEEPRR_BASE}/extend?itemId=${encodeURIComponent(itemId)}"
                     style="margin-left:auto;background:rgba(255,255,255,0.15);
                            padding:4px 10px;border-radius:4px;color:white;
                            text-decoration:none;font-size:12px;" target="_blank" rel="noopener">
                    Keep It
                  </a>
                `;

                const playBtn = document.querySelector('[data-action="play"]')
                  ?? document.querySelector('.mainDetailButtons');
                if (playBtn) {
                  playBtn.insertAdjacentElement('afterend', banner);
                } else {
                  document.body.insertBefore(banner, document.body.firstChild);
                }
              }

              function extractItemId() {
                // Jellyfin's web client uses hash-based routing, e.g. /web/#/details?id=XXX
                const hashQuery = window.location.hash.split('?')[1] ?? '';
                const hashParams = new URLSearchParams(hashQuery);
                const hashId = hashParams.get('id') ?? hashParams.get('itemId');
                if (hashId) return hashId;

                const searchParams = new URLSearchParams(window.location.search);
                return searchParams.get('id') ?? searchParams.get('itemId');
              }

              async function refresh() {
                const itemId = extractItemId();

                if (!itemId) {
                  lastItemId = null;
                  const existing = document.getElementById('sweeprr-banner');
                  if (existing) existing.remove();
                  return;
                }

                if (itemId === lastItemId) return;
                lastItemId = itemId;

                const status = await checkItem(itemId);
                injectBanner(itemId, status);
              }

              const observer = new MutationObserver(() => { refresh(); });
              observer.observe(document.body, { childList: true, subtree: true });

              refresh();
            })();
            """;
    }
}
