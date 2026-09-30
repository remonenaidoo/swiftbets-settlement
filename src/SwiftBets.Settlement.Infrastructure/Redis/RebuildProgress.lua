-- KEYS[1] token set, KEYS[2] leg hash. ARGV: ttl seconds, then pairs of leg id, version. Replaces both from SQL truth.
redis.call('DEL', KEYS[1], KEYS[2])
for i = 2, #ARGV, 2 do
  redis.call('SADD', KEYS[1], ARGV[i] .. ':' .. ARGV[i + 1])
  redis.call('HSET', KEYS[2], ARGV[i], ARGV[i + 1])
end
if #ARGV > 1 then
  redis.call('EXPIRE', KEYS[1], tonumber(ARGV[1]))
  redis.call('EXPIRE', KEYS[2], tonumber(ARGV[1]))
end
return redis.call('HLEN', KEYS[2])
