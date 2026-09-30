-- KEYS[1] token set, KEYS[2] leg hash (leg id -> latest result version). ARGV: token, leg id, version, ttl seconds.
-- Returns {applied, resolved legs}. A token already seen changes nothing.
local added = redis.call('SADD', KEYS[1], ARGV[1])
if added == 1 then
  local current = redis.call('HGET', KEYS[2], ARGV[2])
  if not current or tonumber(current) < tonumber(ARGV[3]) then
    redis.call('HSET', KEYS[2], ARGV[2], ARGV[3])
  end
  redis.call('EXPIRE', KEYS[1], tonumber(ARGV[4]))
  redis.call('EXPIRE', KEYS[2], tonumber(ARGV[4]))
end
return { added, redis.call('HLEN', KEYS[2]) }
