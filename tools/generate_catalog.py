#!/usr/bin/env python3
"""Build a deterministic Hidden Star puzzle catalog from Transfermarkt CSV files."""

from __future__ import annotations

import argparse
import csv
import gzip
import hashlib
import json
import random
from collections import defaultdict
from datetime import date, datetime, timedelta
from pathlib import Path

BIG_FIVE_LEAGUES = {"GB1", "ES1", "IT1", "L1", "FR1"}
FEATURED_CLUBS = {
    "TR1": {"36", "141", "114", "449"},       # Fenerbahçe, Galatasaray, Beşiktaş, Trabzonspor
    "PO1": {"720", "294", "336"},             # Porto, Benfica, Sporting
    "NL1": {"610", "383", "234"},             # Ajax, PSV, Feyenoord
}
LEAGUES = BIG_FIVE_LEAGUES | FEATURED_CLUBS.keys()
POSITION_TR = {
    "Goalkeeper": "Kaleci", "Defender": "Defans", "Midfield": "Orta Saha",
    "Attack": "Forvet", "Centre-Forward": "Santrfor", "Left Winger": "Sol Kanat",
    "Right Winger": "Sağ Kanat", "Centre-Back": "Stoper",
}
COUNTRY_TR = {
    "Turkey": "Türkiye", "Germany": "Almanya", "England": "İngiltere",
    "Netherlands": "Hollanda", "Spain": "İspanya", "Italy": "İtalya",
    "France": "Fransa", "Portugal": "Portekiz", "Brazil": "Brezilya",
    "Argentina": "Arjantin", "Ivory Coast": "Fildişi Sahili",
}
LEAGUE_TR = {
    "premier-league": "Premier Lig", "laliga": "La Liga", "serie-a": "Serie A",
    "bundesliga": "Bundesliga", "ligue-1": "Ligue 1", "super-lig": "Süper Lig",
    "eredivisie": "Eredivisie", "liga-portugal": "Liga Portugal",
}


def open_csv(path: Path):
    opener = gzip.open if path.suffix == ".gz" else open
    return opener(path, "rt", encoding="utf-8-sig", newline="")


def locate(folder: Path, name: str) -> Path:
    for candidate in (folder / f"{name}.csv", folder / f"{name}.csv.gz"):
        if candidate.exists():
            return candidate
    raise FileNotFoundError(f"{name}.csv veya {name}.csv.gz bulunamadı: {folder}")


def rows(folder: Path, name: str):
    with open_csv(locate(folder, name)) as stream:
        yield from csv.DictReader(stream)


def integer(value) -> int:
    try:
        return int(float(value or 0))
    except (TypeError, ValueError):
        return 0


def season_label(season: int) -> str:
    return f"{season}/{str(season + 1)[-2:]}"


def age_on_season_start(birth: str, season: int) -> int:
    if not birth:
        return 0
    born = datetime.fromisoformat(birth[:10]).date()
    return season - born.year - ((8, 1) < (born.month, born.day))


def stable_id(player_id: str, season: int, club_id: str) -> str:
    return f"tm-{player_id}-{season}-{club_id}"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--start-date", type=date.fromisoformat, default=date.today())
    parser.add_argument("--days", type=int, default=365)
    parser.add_argument("--min-appearances", type=int, default=18)
    parser.add_argument("--min-minutes", type=int, default=900)
    parser.add_argument("--min-market-value", type=int, default=20_000_000)
    parser.add_argument("--min-peak-market-value", type=int, default=30_000_000)
    parser.add_argument("--featured-min-market-value", type=int, default=10_000_000)
    parser.add_argument("--featured-min-peak-market-value", type=int, default=20_000_000)
    parser.add_argument("--seed", default="hidden-season-v1")
    args = parser.parse_args()

    players = {r["player_id"]: r for r in rows(args.data, "players")}
    clubs = {r["club_id"]: r for r in rows(args.data, "clubs")}
    competitions = {r["competition_id"]: r for r in rows(args.data, "competitions")}
    games = {
        r["game_id"]: r for r in rows(args.data, "games")
        if r.get("competition_id") in LEAGUES
    }

    totals = defaultdict(lambda: {"apps": 0, "minutes": 0, "goals": 0, "assists": 0})
    for appearance in rows(args.data, "appearances"):
        game = games.get(appearance.get("game_id", ""))
        if not game:
            continue
        player_id = appearance.get("player_id", "")
        club_id = appearance.get("player_club_id") or appearance.get("player_current_club_id", "")
        season = integer(game.get("season"))
        if not player_id or not club_id or not season:
            continue
        key = (player_id, season, club_id, game["competition_id"])
        stat = totals[key]
        stat["apps"] += 1
        stat["minutes"] += integer(appearance.get("minutes_played"))
        stat["goals"] += integer(appearance.get("goals"))
        stat["assists"] += integer(appearance.get("assists"))

    pool = []
    for (player_id, season, club_id, competition_id), stat in totals.items():
        player, club = players.get(player_id), clubs.get(club_id)
        competition = competitions.get(competition_id)
        if not player or not club or not competition:
            continue
        if competition_id in FEATURED_CLUBS and club_id not in FEATURED_CLUBS[competition_id]:
            continue
        market_value = integer(player.get("market_value_in_eur"))
        peak_market_value = integer(player.get("highest_market_value_in_eur"))
        if stat["apps"] < args.min_appearances or stat["minutes"] < args.min_minutes:
            continue
        is_featured_club = competition_id in FEATURED_CLUBS
        min_current = args.featured_min_market_value if is_featured_club else args.min_market_value
        min_peak = args.featured_min_peak_market_value if is_featured_club else args.min_peak_market_value
        if market_value < min_current and peak_market_value < min_peak:
            continue
        name = player.get("name", "").strip()
        if len(name) < 3:
            continue
        position = player.get("sub_position") or player.get("position") or "Bilinmiyor"
        nationality = player.get("country_of_citizenship") or player.get("country_of_birth") or "Bilinmiyor"
        competition_name = competition.get("name") or competition.get("competition_code", "")
        league = LEAGUE_TR.get(competition.get("competition_code", ""), competition_name)
        pool.append({
            "key": stable_id(player_id, season, club_id),
            "answer": name,
            "season": season,
            "clubId": club_id,
            "club": club.get("name", "Bilinmiyor"),
            "league": league,
            "position": POSITION_TR.get(position, position),
            "nationality": COUNTRY_TR.get(nationality, nationality),
            "age": age_on_season_start(player.get("date_of_birth", ""), season),
            "photo": player.get("image_url", ""),
            **stat,
        })

    if not pool:
        raise SystemExit("Filtrelere uyan oyuncu-sezon kaydı bulunamadı.")
    unique_answers = {item["answer"] for item in pool}
    if len(unique_answers) < min(args.days, 30):
        raise SystemExit("Yeterli sayıda farklı oyuncu yok; filtreleri gevşetin.")

    rng = random.Random(hashlib.sha256(args.seed.encode()).digest())
    rng.shuffle(pool)
    selected, used_answers = [], set()
    cursor = 0
    while len(selected) < args.days:
        if cursor >= len(pool):
            cursor = 0
            rng.shuffle(pool)
        item = pool[cursor]
        cursor += 1
        if item["answer"] in used_answers:
            if len(used_answers) < len(unique_answers):
                continue
            used_answers.clear()
        if selected and selected[-1]["answer"] == item["answer"]:
            continue
        selected.append(item)
        used_answers.add(item["answer"])

    candidate_names = sorted({item["answer"] for item in pool})
    puzzles = []
    for index, item in enumerate(selected):
        publish = args.start_date + timedelta(days=index)
        clues = [
            ("position", "Mevki", item["position"], 4, "field"),
            ("age", "O sezonki yaş", str(item["age"]), 5, "age"),
            ("appearances", "Lig maçı", str(item["apps"]), 6, "shirt"),
            ("goals", "Lig golü", str(item["goals"]), 8, "ball"),
            ("assists", "Lig asisti", str(item["assists"]), 8, "assist"),
            ("nationality", "Milliyet", item["nationality"], 10, "flag"),
            ("league", "Lig", item["league"], 16, "trophy"),
            ("club", "Kulüp", item["club"], 20, "badge"),
        ]
        puzzles.append({
            "id": f"{publish.isoformat()}-{item['key']}",
            "number": f"{index + 1:03d}",
            "publishDate": publish.isoformat(),
            "answer": item["answer"],
            "seasonLabel": season_label(item["season"]),
            "photoUrl": item["photo"],
            "clubLogoUrl": f"https://tmssl.akamaized.net/images/wappen/head/{item['clubId']}.png",
            "clues": [
                {"id": i, "label": label, "value": value, "cost": cost, "icon": icon}
                for i, label, value, cost, icon in clues
            ],
        })

    document = {"candidatePlayers": candidate_names, "puzzles": puzzles}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{len(puzzles)} günlük puzzle ve {len(candidate_names)} aday yazıldı: {args.output}")


if __name__ == "__main__":
    main()
