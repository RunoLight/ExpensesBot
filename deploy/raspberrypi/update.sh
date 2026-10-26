#!/bin/sh
# Pulls the latest images and recreates only the containers whose image changed.
# Run every two minutes by expensesbot-update.timer; safe to run by hand.
set -eu
cd /opt/expensesbot

docker compose pull --quiet
docker compose up -d --remove-orphans
docker image prune -f > /dev/null
