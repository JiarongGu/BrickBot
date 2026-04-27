namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Inlined BrickBot script stdlib. Two scripts run before the user's code each Run:
///   - <see cref="InitScript"/> wraps <c>__host</c> into the ergonomic top-level globals.
///   - <see cref="CombatScript"/> ships behavior-tree primitives + helpers under <c>combat.*</c>.
/// </summary>
internal static class StdLib
{
    /// <summary>
    /// Defines the user-facing globals: vision, input, log, wait, isCancelled, now, ctx.
    /// All shape conventions here MUST match what the engine docs / examples rely on.
    /// </summary>
    public const string InitScript = """
(function () {
  'use strict';
  const host = __host;
  const c = __ctx;

  globalThis.vision = {
    /**
     * Find a template inside the current frame.
     * @param {string} templatePath  Path relative to the profile templates dir.
     * @param {{ minConfidence?: number, roi?: {x:number,y:number,w:number,h:number} }=} opts
     * @returns {null | {x:number,y:number,w:number,h:number,cx:number,cy:number,confidence:number}}
     */
    find(templatePath, opts) {
      opts = opts || {};
      const conf = (opts.minConfidence != null) ? opts.minConfidence : 0.85;
      if (opts.roi) {
        const r = opts.roi;
        return host.findTemplateRoi(templatePath, conf, r.x | 0, r.y | 0, r.w | 0, r.h | 0);
      }
      return host.findTemplate(templatePath, conf);
    },

    /** Poll for a template until found or timeout (ms). Returns null on timeout. */
    waitFor(templatePath, timeoutMs, opts) {
      opts = opts || {};
      const conf = (opts.minConfidence != null) ? opts.minConfidence : 0.85;
      return host.waitForTemplate(templatePath, timeoutMs | 0, conf);
    },

    /** Sample BGR color at window-relative (x, y). */
    colorAt(x, y) { return host.colorAt(x | 0, y | 0); },
  };

  globalThis.input = {
    click(x, y, button) { host.click(x | 0, y | 0, button || 'left'); },
    moveTo(x, y) { host.moveTo(x | 0, y | 0); },
    drag(x1, y1, x2, y2, button) { host.drag(x1 | 0, y1 | 0, x2 | 0, y2 | 0, button || 'left'); },
    /** Press + release a virtual-key code (Win32 VK). */
    key(vk) { host.pressKey(vk | 0); },
    keyDown(vk) { host.keyDown(vk | 0); },
    keyUp(vk) { host.keyUp(vk | 0); },
    type(text) { host.typeText(String(text)); },
  };

  globalThis.log = function (msg) { host.log(String(msg)); };

  /** Cooperative sleep — wakes early if Stop is pressed. */
  globalThis.wait = function (ms) { host.waitMs(ms | 0); };

  /** True after the user pressed Stop. Use to break long loops. */
  globalThis.isCancelled = function () { return host.isCancelled(); };

  /** Monotonic millisecond clock (Environment.TickCount64). */
  globalThis.now = function () { return host.now(); };

  /**
   * Shared per-Run key/value store. Library scripts (e.g. perception/monitor helpers)
   * write status here; the main script reads to drive decisions.
   * Values must be JSON-serializable. Cleared at the start of every Run.
   */
  globalThis.ctx = {
    /** Set ctx[key] to any JSON-serializable value. */
    set(key, value) { c.setJson(String(key), JSON.stringify(value === undefined ? null : value)); },
    /** Returns the stored value, or `fallback` (default undefined) if the key is absent. */
    get(key, fallback) {
      const json = c.getJson(String(key));
      if (json === null) return fallback;
      try { return JSON.parse(json); } catch (_) { return fallback; }
    },
    has(key) { return c.has(String(key)); },
    delete(key) { return c.delete(String(key)); },
    keys() { return c.keys(); },
    /** Snapshot of every key/value as a plain object. */
    snapshot() {
      const out = {};
      for (const k of c.keys()) {
        try { out[k] = JSON.parse(c.getJson(k)); } catch (_) { out[k] = null; }
      }
      return out;
    },
    /** Convenience: increment a numeric value (defaults to 0). */
    inc(key, by) {
      const cur = this.get(key, 0);
      const next = (typeof cur === 'number' ? cur : 0) + (by == null ? 1 : by);
      this.set(key, next);
      return next;
    },
  };
})();
""";

    /// <summary>
    /// Behavior-tree primitives on <c>combat.*</c>. Each node is a function
    /// <c>(ctx) =&gt; 'success' | 'failure' | 'running'</c>; composites combine children.
    /// <c>combat.runTree</c> ticks a tree on a fixed interval until cancelled.
    /// </summary>
    public const string CombatScript = """
(function () {
  'use strict';

  const SUCCESS = 'success';
  const FAILURE = 'failure';
  const RUNNING = 'running';

  /** Run children left-to-right; fail/run stops on first non-success. */
  function Sequence(...children) {
    return function (ctx) {
      for (const child of children) {
        const r = child(ctx);
        if (r !== SUCCESS) return r;
      }
      return SUCCESS;
    };
  }

  /** Run children left-to-right; success/run stops on first non-failure. */
  function Selector(...children) {
    return function (ctx) {
      for (const child of children) {
        const r = child(ctx);
        if (r !== FAILURE) return r;
      }
      return FAILURE;
    };
  }

  /** Flip success/failure of a child node. */
  function Inverter(child) {
    return function (ctx) {
      const r = child(ctx);
      if (r === SUCCESS) return FAILURE;
      if (r === FAILURE) return SUCCESS;
      return RUNNING;
    };
  }

  /** Gate a child by a per-instance cooldown. Cooldown only resets on success. */
  function Cooldown(ms, child) {
    let nextAllowed = 0;
    return function (ctx) {
      const t = now();
      if (t < nextAllowed) return FAILURE;
      const r = child(ctx);
      if (r === SUCCESS) nextAllowed = t + ms;
      return r;
    };
  }

  /** Run a side-effect; always succeeds. */
  function Action(fn) {
    return function (ctx) { fn(ctx); return SUCCESS; };
  }

  /** Predicate node — succeeds when fn(ctx) is truthy. */
  function Condition(predicate) {
    return function (ctx) { return predicate(ctx) ? SUCCESS : FAILURE; };
  }

  /**
   * Skill rotation: tries each skill in priority order, respecting per-skill cooldowns.
   * Each skill = { name, cooldown, cast: () => void, ready?: () => boolean }.
   * The first skill whose cooldown is up (and whose `ready` returns true) is cast.
   */
  function SkillRotation(skills) {
    const nodes = skills.map(function (s) {
      const action = Action(function () {
        if (s.name) log('cast ' + s.name);
        s.cast();
      });
      const ready = s.ready ? Sequence(Condition(s.ready), action) : action;
      return Cooldown(s.cooldown, ready);
    });
    return Selector.apply(null, nodes);
  }

  /** Tick a tree at a fixed interval until cancelled (or `limitMs` elapsed). */
  function runTree(tree, opts) {
    opts = opts || {};
    const intervalMs = opts.intervalMs != null ? opts.intervalMs : 50;
    const deadline = opts.limitMs ? now() + opts.limitMs : Infinity;
    while (!isCancelled() && now() < deadline) {
      tree({});
      wait(intervalMs);
    }
  }

  globalThis.combat = {
    SUCCESS: SUCCESS, FAILURE: FAILURE, RUNNING: RUNNING,
    Sequence: Sequence, Selector: Selector, Inverter: Inverter,
    Cooldown: Cooldown, Action: Action, Condition: Condition,
    SkillRotation: SkillRotation, runTree: runTree,
  };
})();
""";
}
