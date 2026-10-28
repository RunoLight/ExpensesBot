# Raspberry Pi deployment runbook

Step by step, from a clean Raspberry Pi to the API answering on a public HTTPS
address through a Cloudflare Tunnel. The result is the setup the main
[README](../../readme.md#production-raspberry-pi--cloudflare-tunnel) describes:
CI builds arm64 images, the Pi pulls them every two minutes, no ports are opened.

Placeholders used below — replace them with your own values:

| Placeholder | Example | Where it is used |
|---|---|---|
| `<owner>` | `runolight` | GitHub account that owns the fork, lower-case (GHCR image names) |
| `<hostname>` | `expensesbot.example.com` | Public API address, a hostname in a domain managed by Cloudflare |
| `<pages-origin>` | `https://<owner>.github.io` | Where GitHub Pages serves the Mini App (CORS) |
| `<pages-url>` | `https://<owner>.github.io/ExpensesBot/` | Mini App URL for the bot's buttons |
| `<tunnel-id>` | `a9a3293a-…` | Printed by `cloudflared tunnel create` |

## 0. What you need

- Raspberry Pi 4 or 5 with a 64-bit OS (Ubuntu Server 24.04+ or Raspberry Pi OS 64-bit), SSH access and `sudo`.
- A domain whose DNS is managed by Cloudflare (a free plan is enough).
- A bot token from [@BotFather](https://t.me/BotFather). Use a **separate** bot for local development: two processes long-polling the same token steal each other's updates.

## 1. Prepare the fork

If you deploy your own fork, change these files and push to `master`:

| File | Change |
|---|---|
| `deploy/raspberrypi/docker-compose.yml` | `ghcr.io/runolight/…` → `ghcr.io/<owner>/…` (both services) |
| `deploy/raspberrypi/cloudflared-config.yml` | `hostname:` → `<hostname>` |
| `ExpensesBot.Client/wwwroot/appsettings.json` | `ApiBaseUrl` → `https://<hostname>/`, `BotUsername` → your bot |
| `ExpensesBot.Api/appsettings.json` | `Cors:AllowedOrigins` → `<pages-origin>` (or set `CLIENT_ORIGIN` in `.env` on the Pi) |

Then in the GitHub repository:

1. **Settings → Pages → Source: GitHub Actions**, so `deploy.yml` can publish the Mini App.
2. Wait for the first `CI` run on `master` to finish: it pushes `ghcr.io/<owner>/expensesbot-api` and `-worker`.
3. **Make both packages public**: your profile → *Packages* → `expensesbot-api` → *Package settings* → *Change visibility* → *Public*; the same for `expensesbot-worker`. The images hold no secrets (the token comes from `.env`), and public images let the Pi pull without credentials.
   To keep them private instead, create a token with `read:packages` and run `sudo docker login ghcr.io -u <owner>` on the Pi once.

## 2. Install Docker, Compose and SQLite

```bash
sudo apt update
sudo apt install -y docker.io docker-compose-v2 sqlite3
sudo systemctl enable --now docker
docker compose version
```

`sqlite3` is needed by `backup.sh` (SQLite's online `.backup`), not by the containers.

## 3. Copy the stack and the secrets

From a checkout of the repository on the Pi (or copy the folder over with `scp`):

```bash
sudo mkdir -p /opt/expensesbot
sudo cp deploy/raspberrypi/docker-compose.yml deploy/raspberrypi/update.sh deploy/raspberrypi/backup.sh /opt/expensesbot/
sudo chmod 755 /opt/expensesbot/update.sh /opt/expensesbot/backup.sh

sudo cp deploy/raspberrypi/.env.example /opt/expensesbot/.env
sudo chown root:root /opt/expensesbot/.env
sudo chmod 600 /opt/expensesbot/.env
sudo nano /opt/expensesbot/.env   # TELEGRAM_BOT_TOKEN, CLIENT_ORIGIN, WEBAPP_URL
```

`.env` holds the bot token: it is readable by root only, and the systemd units that use it run as root. Edit it with `sudo`.

## 4. Cloudflare Tunnel

`cloudflared` runs as a service of the compose stack (image `cloudflare/cloudflared`), nothing is installed on the host. Its config and the tunnel credentials live in `/opt/expensesbot/cloudflared`, owned by the image's `nonroot` user (uid 65532). The one-off setup commands go through the `cloudflared-cli` service, which `up` never starts. From the checkout, as in step 3:

```bash
sudo install -d -o 65532 -g 65532 -m 700 /opt/expensesbot/cloudflared
sudo cp deploy/raspberrypi/cloudflared-config.yml /opt/expensesbot/cloudflared/config.yml

cd /opt/expensesbot
sudo docker compose run --rm cloudflared-cli tunnel login                            # opens a link; pick the domain. Saves cloudflared/cert.pem
sudo docker compose run --rm cloudflared-cli tunnel create expensesbot               # prints <tunnel-id>, writes cloudflared/<tunnel-id>.json
sudo docker compose run --rm cloudflared-cli tunnel route dns expensesbot <hostname> # creates the CNAME in Cloudflare DNS
sudo nano cloudflared/config.yml                        # replace <TUNNEL_ID> (twice) and the hostname
```

The ingress points at `http://api:8080`, the API container on the compose network. `<tunnel-id>.json` lets anyone run your tunnel and `cert.pem` lets them manage tunnels and DNS for the zone: keep copies in a password manager, never in git.

**Moving from `cloudflared` on the host** (earlier versions of this runbook): copy the existing credentials, start the container, then remove the host service. The tunnel and its DNS record stay as they are, and both connectors serve the tunnel for a moment, so there is no downtime.

```bash
sudo cp deploy/raspberrypi/docker-compose.yml /opt/expensesbot/      # the version with the cloudflared service
sudo install -d -o 65532 -g 65532 -m 700 /opt/expensesbot/cloudflared
sudo cp /etc/cloudflared/<tunnel-id>.json /opt/expensesbot/cloudflared/
sudo cp deploy/raspberrypi/cloudflared-config.yml /opt/expensesbot/cloudflared/config.yml
sudo nano /opt/expensesbot/cloudflared/config.yml      # <TUNNEL_ID> (twice) and the hostname
sudo chown 65532:65532 /opt/expensesbot/cloudflared/*
sudo systemctl start expensesbot-update                 # starts the cloudflared container
sudo docker compose -f /opt/expensesbot/docker-compose.yml logs cloudflared   # expect "Registered tunnel connection"
sudo cloudflared service uninstall                      # stops and removes the host service
sudo apt remove cloudflared                             # optional
```

## 5. First start

```bash
cd /opt/expensesbot
sudo docker compose pull
sudo docker compose up -d
sudo docker compose ps
curl -s http://127.0.0.1:8080/health
curl -s https://<hostname>/health
```

`/health` should report `database: Healthy` and `telegram: Healthy`. The API applies the database migrations on its first start. If the public address doesn't answer, check `sudo docker compose logs cloudflared`.

## 6. Automatic updates and backups

```bash
sudo cp deploy/raspberrypi/expensesbot-*.service deploy/raspberrypi/expensesbot-*.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now expensesbot-update.timer expensesbot-backup.timer
systemctl list-timers 'expensesbot*'
```

- `expensesbot-update.timer` runs `update.sh` every two minutes: `docker compose pull`, `up -d` (recreates only containers whose image changed), prune old images.
- `expensesbot-backup.timer` runs `backup.sh` daily at 04:30: a consistent copy to `/opt/expensesbot/backups/expenses-YYYY-MM-DD.db`, keeping the last 14.

Run each once by hand to check: `sudo systemctl start expensesbot-update expensesbot-backup`, then `journalctl -u expensesbot-update -u expensesbot-backup -n 20`.

The backups sit on the same SD card as the database, so they don't survive a dead card or a lost Pi. Copy them off the device as well — for example with [restic](https://restic.net) to an encrypted repository in cloud storage, on its own timer.

## 7. Telegram

1. Open the Mini App URL (`<pages-url>`) once in a browser to check that GitHub Pages serves it.
2. In @BotFather: `/mybots` → your bot → *Bot Settings* → *Menu Button* → set `<pages-url>`; or create a Mini App with `/newapp`.
3. Send `/start` to the bot and open the app from the menu button.

## Operations

| Task | Command |
|---|---|
| Logs | `cd /opt/expensesbot && sudo docker compose logs -f api` (or `worker`, `cloudflared`) |
| Updater log | `journalctl -u expensesbot-update -n 50` |
| Deploy now | `sudo systemctl start expensesbot-update` |
| Health | `curl -s https://<hostname>/health` |
| Running version | `sudo docker inspect expensesbot-api-1 --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}'` |

### Roll back

Set `IMAGE_TAG=<commit sha>` in `/opt/expensesbot/.env` and run `sudo systemctl start expensesbot-update`; set it back to `latest` to follow `master` again.

Migrations only go forward: if the newer version added a migration, the older image can't read the upgraded database. In that case restore the backup taken before the deploy (below) together with the rollback.

### Restore a backup

```bash
cd /opt/expensesbot
ls -1 backups/                                          # pick a file
sqlite3 backups/expenses-2026-10-27.db 'PRAGMA integrity_check;'   # expect "ok"

sudo docker compose down
db=$(sudo docker volume inspect expensesbot_expenses-data --format '{{ .Mountpoint }}')/expenses.db
sudo cp "$db" "backups/before-restore-$(date +%F-%H%M).db"   # keep the current state, just in case
sudo cp backups/expenses-2026-10-27.db "$db"
sudo rm -f "$db-wal" "$db-shm"                          # the old write-ahead log belongs to the replaced file
sudo chown 1654:1654 "$db"                              # the containers' "app" user (APP_UID of the .NET images)
sudo docker compose up -d
curl -s http://127.0.0.1:8080/health
```

Transactions recorded after the backup was taken are lost; the bot and the Mini App keep working with the restored data.
