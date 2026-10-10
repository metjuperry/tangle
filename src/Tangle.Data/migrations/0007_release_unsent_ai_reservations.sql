-- This exact validation error occurred before dispatch, so these reservations incurred no provider cost.
UPDATE ai_call SET status='failed',reserved_cost=0,cost=0
WHERE status='reserved' AND error='AI input exceeds the reserved-cost safety limit.';
