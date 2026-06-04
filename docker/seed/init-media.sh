#!/usr/bin/env sh
# Seed the shared `media` Docker volume with dummy directory structure and
# placeholder files so Jellyfin, Radarr, and Sonarr can see "libraries"
# without requiring real media.
#
# Usage (run once after `docker compose up`):
#   docker compose run --rm -v sweeprr_media:/media sweeprr sh docker/seed/init-media.sh
#
# Or on Linux/macOS with a bind-mounted volume:
#   docker run --rm -v sweeprr_media:/media alpine sh /init-media.sh

set -e

MOVIES_DIR="/media/movies"
TV_DIR="/media/tv"

mkdir -p "$MOVIES_DIR" "$TV_DIR"

# ── Dummy movies ──────────────────────────────────────────────────────────────
for movie in \
    "The Test Film (2020)" \
    "Another Fake Movie (2021)" \
    "Placeholder Feature (2022)"; do
    dir="$MOVIES_DIR/$movie"
    mkdir -p "$dir"
    # Zero-byte mkv — Jellyfin/Radarr detect by filename, not content
    touch "$dir/${movie}.mkv"
done

# ── Dummy TV shows ────────────────────────────────────────────────────────────
for show in "Fake Series" "Another Test Show"; do
    for season in 1 2; do
        dir="$TV_DIR/$show/Season $season"
        mkdir -p "$dir"
        for ep in 1 2 3; do
            touch "$dir/${show} - S0${season}E0${ep}.mkv"
        done
    done
done

echo "Seed complete."
echo "Movies : $(find $MOVIES_DIR -name '*.mkv' | wc -l)"
echo "Episodes: $(find $TV_DIR    -name '*.mkv' | wc -l)"
