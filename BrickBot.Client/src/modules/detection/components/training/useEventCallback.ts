import { useCallback, useEffect, useRef } from 'react';

/**
 * Stable-identity callback hook — wraps a possibly-changing function in a ref so the
 * returned callback never changes identity but always invokes the latest impl.
 *
 * Use case: passing handlers to `React.memo`'d list rows. Without this, every parent
 * re-render creates fresh closures → memo's prop-equality check sees new function
 * references → all rows re-render. With this, the wrapper stays referentially stable,
 * so memo short-circuits the rows whose actual data didn't change.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function useEventCallback<T extends (...args: any[]) => any>(fn: T): T {
  const ref = useRef(fn);
  useEffect(() => { ref.current = fn; });
  return useCallback((...args: Parameters<T>) => ref.current(...args), []) as T;
}
