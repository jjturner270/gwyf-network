class TokenBucket {
  private tokens: number;
  private lastRefill: number;

  constructor(
    private capacity: number,
    private refillPerMs: number,
  ) {
    this.tokens = capacity;
    this.lastRefill = Date.now();
  }

  take(now = Date.now()): boolean {
    // A clock that ever moves backward (test clock, NTP, wall-clock races) must
    // never drain the bucket: refill is clamped to >= 0 and lastRefill never
    // regresses, otherwise a single backward step trips a permanent 429.
    const elapsed = Math.max(0, now - this.lastRefill);
    this.tokens = Math.min(this.capacity, this.tokens + (elapsed * this.refillPerMs) / 1000);
    this.lastRefill = Math.max(this.lastRefill, now);
    if (this.tokens < 1) return false;
    this.tokens -= 1;
    return true;
  }
}

export class RateLimiter {
  private buckets = new Map<string, TokenBucket>();

  constructor(
    private perMinute: number,
    private maxBurst = 60,
    private cleanupEvery = 10_000,
  ) {}

  allow(key: string, now = Date.now()): boolean {
    let bucket = this.buckets.get(key);
    if (!bucket) {
      bucket = new TokenBucket(this.maxBurst, this.perMinute / 60_000);
      this.buckets.set(key, bucket);
      if (this.buckets.size > this.cleanupEvery) this.cleanup(now);
    }
    return bucket.take(now);
  }

  private cleanup(now: number) {
    for (const [key, bucket] of this.buckets) {
      if (bucket.take(now)) this.buckets.delete(key);
    }
  }
}