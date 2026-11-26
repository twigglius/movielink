-- Puzzles part of client/Assets/_Game/Resources/fake-data.json (first 40 of the fixture pool).
-- psql -At $DB -f tools/fixtures/export_fake_data.sql > /tmp/puzzles.json, then merge with seed-dataset.json.
SELECT json_agg(json_build_object('start', start_actor_id, 'target', target_actor_id,
       'optimalLinks', optimal_links, 'examplePath', example_path) ORDER BY pool_index)
FROM (SELECT * FROM puzzles WHERE pool_index < 40 AND dataset_version = 1 AND tier = 1) p;
