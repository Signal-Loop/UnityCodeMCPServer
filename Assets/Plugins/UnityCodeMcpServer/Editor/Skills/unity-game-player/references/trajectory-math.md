# Trajectory & Math Patterns

Reusable mathematical patterns for computing game actions. All examples are game-agnostic — adapt variable names to whatever the INIT phase discovers.

## Table of Contents

1. Linear Interception (1D)
2. Bounce Prediction (2D with reflections)
3. Projectile Interception (2D)
4. Duration Calculation
5. Pre-positioning When Target Moves Away
6. Multi-entity Priority Selection

---

## 1. Linear Interception (1D)

When a target moves along one axis and the player moves along the same axis to intercept.

```csharp
// SENSE
float targetPos = /* target position on shared axis */;
float targetVel = /* target velocity on shared axis */;
float playerPos = /* player position on shared axis */;
float playerSpeed = /* player movement speed (always positive) */;
float interceptAxis = /* fixed coordinate where interception happens (e.g., player's X) */;

// COMPUTE
float timeToArrive = (targetVel != 0f)
    ? (interceptAxis - targetPos) / targetVel   // <0 means moving away
    : 999f;

float predictedPos = targetPos + targetVel * Mathf.Max(timeToArrive, 0f);
float delta = predictedPos - playerPos;
float durationMs = Mathf.Abs(delta) / playerSpeed * 1000f;
string direction = delta > 0 ? "Up" : "Down"; // adapt axis names

Debug.Log($"COMPUTE: delta={delta} dir={direction} duration={durationMs}ms");
```

---

## 2. Bounce Prediction (2D with reflections)

When a target bounces between parallel boundaries (e.g., ball between top/bottom walls). The fold/unfold technique simulates infinite reflections without iterating bounces.

```csharp
// SENSE
float bx = /* target X */;  float by = /* target Y */;
float vx = /* target velocity X */;  float vy = /* target velocity Y */;
float playerY = /* player Y position */;

// INIT constants
float interceptX = /* X coordinate where player intercepts */;
float topBound = /* top boundary Y */;
float botBound = /* bottom boundary Y */;
float playerSpeed = /* player movement speed */;
float playerMinY = /* player clamp min Y */;
float playerMaxY = /* player clamp max Y */;

// COMPUTE — time to arrival at intercept X
float t = (vx != 0f) ? (interceptX - bx) / vx : 999f;

if (t < 0f) {
    // Target moving away — use pre-positioning (see section 5)
    t = 999f;
}

// Raw predicted Y without boundary clamping
float rawY = by + vy * t;

// Fold/unfold to simulate bounces
float height = Mathf.Abs(topBound - botBound);
float shifted = rawY - botBound;
float folded = shifted % (2f * height);
if (folded < 0f) folded += 2f * height;

float targetY;
if (folded <= height)
    targetY = botBound + folded;
else
    targetY = topBound - (folded - height);

// Clamp to player movement range
targetY = Mathf.Clamp(targetY, playerMinY, playerMaxY);

float delta = targetY - playerY;
float durationMs = Mathf.Abs(delta) / playerSpeed * 1000f;
string direction = delta > 0 ? "Up" : "Down";

Debug.Log($"SENSE: target=({bx},{by}) vel=({vx},{vy}) playerY={playerY}");
Debug.Log($"COMPUTE: t={t:F3}s rawY={rawY:F2} targetY={targetY:F2}");
Debug.Log($"COMPUTE: delta={delta:F3} dir={direction} duration={durationMs:F1}ms");
```

### Why fold/unfold works

Instead of simulating each bounce step-by-step, we:

1. Project the raw Y as if boundaries didn't exist.
2. Shift so `botBound = 0`.
3. Modulo by `2 * height` to get position within one "fold cycle."
4. If `folded <= height`, the target is in a forward pass; otherwise it's in a reflected pass.

This handles any number of bounces in O(1).

---

## 3. Projectile Interception (2D)

When both the target and the interceptor move in 2D and you need to compute the interception point.

```csharp
// SENSE
Vector2 targetPos = /* target position */;
Vector2 targetVel = /* target velocity */;
Vector2 playerPos = /* player position */;
float playerSpeed = /* player speed (scalar) */;

// COMPUTE — quadratic solution for interception time
// |targetPos + targetVel*t - playerPos| = playerSpeed * t
Vector2 diff = targetPos - playerPos;
float a = Vector2.Dot(targetVel, targetVel) - playerSpeed * playerSpeed;
float b = 2f * Vector2.Dot(diff, targetVel);
float c = Vector2.Dot(diff, diff);

float disc = b * b - 4f * a * c;
float t = -1f;

if (Mathf.Abs(a) < 0.0001f) {
    // Linear case
    t = -c / b;
} else if (disc >= 0f) {
    float sqrtDisc = Mathf.Sqrt(disc);
    float t1 = (-b - sqrtDisc) / (2f * a);
    float t2 = (-b + sqrtDisc) / (2f * a);
    if (t1 > 0f) t = t1;
    else if (t2 > 0f) t = t2;
}

if (t > 0f) {
    Vector2 interceptPoint = targetPos + targetVel * t;
    Vector2 moveDir = (interceptPoint - playerPos).normalized;
    float distance = Vector2.Distance(playerPos, interceptPoint);
    float durationMs = distance / playerSpeed * 1000f;
    Debug.Log($"COMPUTE: intercept={interceptPoint} t={t:F3}s duration={durationMs:F1}ms");
} else {
    Debug.Log("COMPUTE: no interception possible — idle");
}
```

---

## 4. Duration Calculation

The fundamental formula used across all patterns:

```
durationMs = abs(delta) / speed * 1000
```

Where:

- `delta` = target position minus current position (on the movement axis).
- `speed` = entity movement speed in units/second.
- Result is in milliseconds, suitable for `play_unity_game` `duration` parameter.

### Critical rules

- **Never round up** to "nice" numbers. If math says 23.7 ms, use 24 ms (integer ceiling at most).
- **Never apply minimums.** A 0.1-unit delta at speed 8 = 12.5 ms. Forcing 200 ms overshoots by 16x.
- **Never apply maximums** unless the game physically caps movement.
- **Convert to integer** for the tool parameter: `(int)Mathf.Ceil(durationMs)` or `Mathf.RoundToInt(durationMs)`.

---

## 5. Pre-positioning When Target Moves Away

When the target is heading away from the player (e.g., ball moving left while player is on the right), compute where it will arrive on the return trip and move there early.

```csharp
// If t < 0 (target moving away), estimate return trajectory.
// Strategy: use the fold/unfold formula with a large t to predict
// where the target will be when it eventually returns.

// Option A: Use current trajectory angle preserved after wall reflection.
// The fold/unfold math (section 2) already handles this — just use t = 999.
// The result is the Y position where the ball will arrive after bouncing back.

// Option B: Move to center of play area to minimize max distance to any arrival point.
float centerY = (topBound + botBound) / 2f;
float delta = centerY - playerY;
float durationMs = Mathf.Abs(delta) / playerSpeed * 1000f;
```

**Prefer Option A** (predictive) when velocity is known and reliable.
**Use Option B** (center) only when trajectory is unpredictable (random spawns, teleportation).

---

## 6. Multi-entity Priority Selection

When multiple targets exist (e.g., multiple balls, enemies), select the most urgent one.

```csharp
// Find the entity arriving soonest at the player's intercept line
float closestTime = float.MaxValue;
GameObject mostUrgent = null;

foreach (var entity in entities) {
    var rb = entity.GetComponent<Rigidbody2D>();
    float vx = rb.linearVelocity.x;
    float ex = entity.transform.position.x;

    if (vx == 0f) continue; // stationary — skip

    float t = (interceptX - ex) / vx;
    if (t > 0f && t < closestTime) {
        closestTime = t;
        mostUrgent = entity;
    }
}

if (mostUrgent != null) {
    Debug.Log($"PRIORITY: {mostUrgent.name} arrives in {closestTime:F3}s");
    // Proceed with COMPUTE for mostUrgent
} else {
    Debug.Log("PRIORITY: no incoming targets — idle");
}
```
