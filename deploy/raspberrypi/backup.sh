#!/bin/sh
# Consistent SQLite backup (online .backup API, safe while the app writes),
# keeps the last 14 daily copies. Run daily by expensesbot-backup.timer.
set -eu
dest=/opt/expensesbot/backups
src=$(docker volume inspect expensesbot_expenses-data --format '{{ .Mountpoint }}')/expenses.db

mkdir -p "$dest"
sqlite3 "$src" ".backup '$dest/expenses-$(date +%F).db'"
ls -1t "$dest"/expenses-*.db | tail -n +15 | xargs -r rm --
