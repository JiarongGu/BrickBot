import { useCallback, useRef, useState } from 'react';

/**
 * useDelayedLoading — show a loading flag only if the wrapped operation takes longer
 * than `delayMs` (default 200). Avoids spinner-flicker on fast operations.
 *
 * Returns:
 * - `loading` — true while the op runs AND the delay has elapsed
 * - `execute(fn)` — runs `fn` with the delay-loading semantics; throws "Operation
 *   already in progress" if you call it again before the previous resolves
 * - `reset()` — clear the loading flag (use in a `useEffect` cleanup when the dialog closes)
 */
export function useDelayedLoading(delayMs = 200) {
  const [loading, setLoading] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const inFlightRef = useRef(false);

  const reset = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = undefined;
    }
    inFlightRef.current = false;
    setLoading(false);
  }, []);

  const execute = useCallback(async <T,>(fn: () => Promise<T>): Promise<T> => {
    if (inFlightRef.current) {
      throw new Error('Operation already in progress');
    }
    inFlightRef.current = true;
    timerRef.current = setTimeout(() => setLoading(true), delayMs);
    try {
      return await fn();
    } finally {
      if (timerRef.current) {
        clearTimeout(timerRef.current);
        timerRef.current = undefined;
      }
      inFlightRef.current = false;
      setLoading(false);
    }
  }, [delayMs]);

  return { loading, execute, reset };
}
