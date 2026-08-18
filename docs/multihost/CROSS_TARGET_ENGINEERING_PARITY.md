# CROSS_TARGET_ENGINEERING_PARITY

Same neutral Example-1 inputs, same shared engineering DLLs, run under BOTH shipping
targets (net8 runner in the 2026 payload; net10 runner in the 2027 payload), each from a
clean copy of its exact shipping folder:

- movements=11, conflicts=50, VALID=24, REVIEW_REQUIRED=0, BLOCKED=0, W-L to S-T = 5 (both)
- Deterministic artifacts compared BYTE-FOR-BYTE between targets:
  - EXAMPLE1.analysis.json — IDENTICAL (91,962 bytes): every conflict id, CD, ED, raw IG,
    final IG, matrix cell and status
  - EXAMPLE1.validation.json — IDENTICAL (3,460 bytes): all findings
- No tolerances needed; no target-year-specific expected values exist anywhere.
