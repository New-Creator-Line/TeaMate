import os
import sys
import time
import requests

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# NOTE: Do NOT run this long-polling bridge while Cloudflare Webhook mode is active,
# because getUpdates / deleteWebhook will disable the registered Telegram webhook.
TOKEN = os.getenv("TELEGRAM_BOT_TOKEN", "")
WEBHOOK_SECRET = os.getenv("TELEGRAM_WEBHOOK_SECRET", "")
if not TOKEN:
    raise RuntimeError("TELEGRAM_BOT_TOKEN environment variable is required.")

GATEWAY_URL = os.getenv("GATEWAY_URL", "http://localhost:8088/api/telegram/webhook")
TG_API = f"https://api.telegram.org/bot{TOKEN}"


def sanitize_err(err: Exception) -> str:
    return str(err).replace(TOKEN, "***BOT_TOKEN***")


print("=" * 60)
print("Fotoprint Telegram Fallback Long-Polling Bridge (Dev Only)")
print("=" * 60)

try:
    requests.post(f"{TG_API}/deleteWebhook", timeout=10)
    print("Telegram webhook cleared, Long Polling mode enabled.")
except Exception as e:
    print(f"Error resetting webhook: {sanitize_err(e)}")

offset = 0
try:
    resp = requests.get(f"{TG_API}/getUpdates", timeout=10).json()
    if resp.get("ok") and resp.get("result"):
        offset = resp["result"][-1]["update_id"] + 1
        print(f"Initial update offset: {offset}")
except Exception as e:
    print(f"Error getting initial offset: {sanitize_err(e)}")

print(f"Listening for updates -> forwarding to {GATEWAY_URL}...")
gw_headers = {"X-Telegram-Bot-Api-Secret-Token": WEBHOOK_SECRET} if WEBHOOK_SECRET else {}

while True:
    try:
        r = requests.get(f"{TG_API}/getUpdates", params={"offset": offset, "timeout": 20}, timeout=25)
        data = r.json()
        if not data.get("ok"):
            time.sleep(2)
            continue

        for update in data.get("result", []):
            update_id = update["update_id"]
            offset = max(offset, update_id + 1)

            if "message" in update and "chat" in update["message"]:
                chat_id = update["message"]["chat"]["id"]
                user = update["message"].get("from", {}).get("username", "user")
                txt = update["message"].get("text", "")
                print(f"\n[INCOMING MESSAGE] from @{user} ({chat_id}): '{txt}'")

            # Forward to .NET 9 Gateway (delivers reply asynchronously via Telegram Bot API)
            try:
                gw_resp = requests.post(GATEWAY_URL, json=update, headers=gw_headers, timeout=30)
                if gw_resp.status_code == 200:
                    print(f"[FORWARDED] {gw_resp.text}")
                else:
                    print(f"[GATEWAY ERROR] HTTP {gw_resp.status_code}: {gw_resp.text}")
            except Exception as ex:
                print(f"[GATEWAY EXCEPTION] {sanitize_err(ex)}")

    except requests.exceptions.Timeout:
        continue
    except KeyboardInterrupt:
        print("\nStopping Telegram Bridge...")
        break
    except Exception as e:
        print(f"[POLL ERROR] {sanitize_err(e)}")
        time.sleep(3)
