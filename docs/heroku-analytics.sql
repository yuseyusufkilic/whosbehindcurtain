-- Daily unique players and completed games (last 30 days)
SELECT
    occurred_at::date AS day,
    count(DISTINCT subject_hash) FILTER (WHERE event_type = 'puzzle_started') AS players,
    count(*) FILTER (WHERE event_type = 'game_completed') AS completed_games
FROM hidden_star_events
WHERE occurred_at >= now() - interval '30 days'
GROUP BY 1
ORDER BY 1 DESC;

-- Puzzle difficulty and average score
SELECT
    puzzle_id,
    count(*) AS plays,
    round(100.0 * avg(CASE WHEN solved THEN 1 ELSE 0 END), 1) AS solve_rate_pct,
    round(avg(score), 1) AS average_score
FROM hidden_star_events
WHERE event_type = 'game_completed'
GROUP BY puzzle_id
ORDER BY solve_rate_pct, average_score;

-- Guess distribution and overall success
SELECT
    count(*) AS guesses,
    round(100.0 * avg(CASE WHEN solved THEN 1 ELSE 0 END), 1) AS correct_guess_pct,
    round(avg(3 - attempts_left + CASE WHEN solved THEN 1 ELSE 0 END), 2) AS average_guess_number
FROM hidden_star_events
WHERE event_type = 'guess_submitted';

-- Most frequently revealed clues
SELECT event_key AS clue, count(*) AS reveals
FROM hidden_star_events
WHERE event_type = 'clue_revealed'
GROUP BY event_key
ORDER BY reveals DESC;

-- Share conversion among completed games
SELECT
    count(*) FILTER (WHERE event_type = 'game_completed') AS completed,
    count(*) FILTER (WHERE event_type = 'result_shared') AS shared,
    round(
        100.0 * count(*) FILTER (WHERE event_type = 'result_shared')
        / nullif(count(*) FILTER (WHERE event_type = 'game_completed'), 0),
        1
    ) AS share_rate_pct
FROM hidden_star_events;

-- Optional maintenance: retain raw events for one year.
-- DELETE FROM hidden_star_events WHERE occurred_at < now() - interval '1 year';
