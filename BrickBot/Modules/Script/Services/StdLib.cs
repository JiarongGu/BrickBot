namespace BrickBot.Modules.Script.Services;

/// <summary>
/// Inlined BrickBot script stdlib. Two scripts run before the user's code each Run:
///   - <see cref="InitScript"/> wraps <c>__host</c> into the ergonomic top-level globals
///     plus the <c>brickbot</c> event-bus / action-registry / trigger API + tick loop.
///   - <see cref="CombatScript"/> ships behavior-tree primitives + helpers under <c>combat.*</c>.
/// </summary>
internal static class StdLib
{
    /// <summary>
    /// Defines the user-facing globals: vision, input, log, wait, isCancelled, now, ctx, brickbot.
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
      const scale = (opts.scale != null) ? opts.scale : 1.0;
      const gray = !!opts.grayscale;
      const pyramid = !!opts.pyramid;
      if (opts.roi) {
        const r = opts.roi;
        return host.findTemplateRoi(templatePath, conf, r.x | 0, r.y | 0, r.w | 0, r.h | 0, scale, gray, pyramid);
      }
      return host.findTemplate(templatePath, conf, scale, gray, pyramid);
    },

    /** Poll for a template until found or timeout (ms). Returns null on timeout. */
    waitFor(templatePath, timeoutMs, opts) {
      opts = opts || {};
      const conf = (opts.minConfidence != null) ? opts.minConfidence : 0.85;
      const scale = (opts.scale != null) ? opts.scale : 1.0;
      const gray = !!opts.grayscale;
      const pyramid = !!opts.pyramid;
      return host.waitForTemplate(templatePath, timeoutMs | 0, conf, scale, gray, pyramid);
    },

    /**
     * Scale-invariant template match. Tries scaleSteps scales between scaleMin..scaleMax
     * and returns the best. Solves "icon scaled because resolution changed" without ORB's
     * native-feature-extractor dep. Combine with a tight roi to keep total cost low.
     * @param {string} templatePath
     * @param {{ minConfidence?: number, scaleMin?: number, scaleMax?: number, scaleSteps?: number, roi?: {x,y,w,h} }=} opts
     */
    findFeatures(templatePath, opts) {
      opts = opts || {};
      const conf = (opts.minConfidence != null) ? opts.minConfidence : 0.80;
      const sMin = (opts.scaleMin != null) ? opts.scaleMin : 0.9;
      const sMax = (opts.scaleMax != null) ? opts.scaleMax : 1.1;
      const sSteps = (opts.scaleSteps != null) ? (opts.scaleSteps | 0) : 3;
      if (opts.roi) {
        const r = opts.roi;
        return host.findFeatures(templatePath, conf, sMin, sMax, sSteps, true, r.x | 0, r.y | 0, r.w | 0, r.h | 0);
      }
      return host.findFeatures(templatePath, conf, sMin, sMax, sSteps, false, 0, 0, 0, 0);
    },

    /**
     * Find blobs of a color range. Returns array of {x,y,w,h,area,cx,cy} sorted by area desc.
     * Order of magnitude faster than template matching for distinctly-colored elements.
     * @param {{ rMin,rMax,gMin,gMax,bMin,bMax }} range
     * @param {{ roi?, minArea?: number, maxResults?: number }=} opts
     */
    findColors(range, opts) {
      opts = opts || {};
      const minArea = (opts.minArea != null) ? (opts.minArea | 0) : 25;
      const maxResults = (opts.maxResults != null) ? (opts.maxResults | 0) : 32;
      if (opts.roi) {
        const r = opts.roi;
        return host.findColors(range.rMin | 0, range.rMax | 0, range.gMin | 0, range.gMax | 0,
                               range.bMin | 0, range.bMax | 0,
                               true, r.x | 0, r.y | 0, r.w | 0, r.h | 0,
                               minArea, maxResults);
      }
      return host.findColors(range.rMin | 0, range.rMax | 0, range.gMin | 0, range.gMax | 0,
                             range.bMin | 0, range.bMax | 0,
                             false, 0, 0, 0, 0, minArea, maxResults);
    },

    /** Sample BGR color at window-relative (x, y). */
    colorAt(x, y) { return host.colorAt(x | 0, y | 0); },

    /**
     * HP / MP / cooldown bar fill ratio. Returns 0..1.
     * @param {{x,y,w,h}} roi
     * @param {{r,g,b}} color  Target fill color (BGR or RGB; matched per channel).
     * @param {{ tolerance?: number }=} opts  Per-channel ±tolerance, default 25.
     */
    percentBar(roi, color, opts) {
      opts = opts || {};
      const tol = (opts.tolerance != null) ? (opts.tolerance | 0) : 25;
      return host.percentBar(roi.x | 0, roi.y | 0, roi.w | 0, roi.h | 0,
                             color.r | 0, color.g | 0, color.b | 0, tol);
    },

    /**
     * Snapshot a ROI into a named baseline. Subsequent vision.diff(name, roi) calls
     * return how much that ROI has changed since the snapshot.
     */
    captureBaseline(name, roi) {
      host.captureBaseline(String(name), roi.x | 0, roi.y | 0, roi.w | 0, roi.h | 0);
    },

    /**
     * Mean absolute pixel difference (0..1) between the named baseline and the same
     * ROI in the current frame. 0 = no change, 1 = totally different. Returns 1.0
     * when no baseline is registered under that name.
     */
    diff(name, roi) {
      return host.diffBaseline(String(name), roi.x | 0, roi.y | 0, roi.w | 0, roi.h | 0);
    },

    /** Clear all baselines. */
    clearBaselines() { host.clearBaselines(); },
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

  // ---------------- detect — typed, persisted vision rules ----------------
  //
  // Each definition lives at data/profiles/{id}/detections/{id}.json (managed by the UI).
  // The host exposes:
  //   __host.listDetections()             → [DetectionDefinition]
  //   __host.runDetection(id)             → DetectionResult
  //   __host.runDetectionDefinition(d)    → DetectionResult  (inline, for live preview)
  //   __host.resetDetections()
  //
  // The wrapper below applies the def.output bindings (ctx writes + brickbot.emit) so users
  // don't have to remember which key feeds where.

  let __detectionDefs = null;
  let __detectionLast = Object.create(null);  // id → last result value (for eventOnChangeOnly)

  function __loadDetectionDefs() {
    if (__detectionDefs === null) __detectionDefs = host.listDetections();
    return __detectionDefs;
  }

  function __findDef(idOrName) {
    const defs = __loadDetectionDefs();
    for (const d of defs) {
      if (d.id === idOrName || d.name === idOrName) return d;
    }
    return null;
  }

  /** Pick the value that gets written to ctx / sent as the event payload. */
  function __resultValue(result) {
    if (result.value != null) return result.value;
    if (result.triggered != null) return result.triggered;
    return result.found;
  }

  function __applyOutput(def, result) {
    const out = def.output;
    if (!out) return;
    const value = __resultValue(result);
    if (out.ctxKey) ctx.set(out.ctxKey, value);
    if (out.event) {
      const prev = __detectionLast[def.id];
      const changed = prev === undefined || prev !== value;
      if (!out.eventOnChangeOnly || changed) {
        brickbot.emit(out.event, { id: def.id, name: def.name, value: value, result: result });
      }
    }
    __detectionLast[def.id] = value;
  }

  globalThis.detect = {
    /** Force a re-read of the detection definitions from disk (e.g. after the UI saves one). */
    reload() {
      __detectionDefs = null;
      __detectionLast = Object.create(null);
      host.resetDetections();
    },

    /** All loaded definitions (cached). */
    list() { return __loadDetectionDefs().slice(); },

    /**
     * Run one detection by id (or name) against the current shared frame.
     * Applies the definition's output bindings (ctx + event) before returning.
     */
    run(idOrName) {
      const def = __findDef(idOrName);
      if (!def) throw new Error('DETECTION_NOT_FOUND: ' + idOrName);
      const r = host.runDetection(def.id);
      __applyOutput(def, r);
      return r;
    },

    /**
     * Run every enabled definition. Two-pass to honor cross-detection ROI refs:
     *   pass 1 — definitions whose ROI doesn't depend on another detection
     *   pass 2 — definitions whose ROI uses fromDetectionId (parent already resolved)
     * Cheap enough to call each tick; pair with `brickbot.runForever({ autoDetect: true })`.
     */
    runAll() {
      const defs = __loadDetectionDefs();
      const out = [];
      const ranIds = Object.create(null);

      const runOne = (def) => {
        if (def.enabled === false) return;
        try {
          const r = host.runDetection(def.id);
          __applyOutput(def, r);
          out.push(r);
          ranIds[def.id] = true;
        } catch (e) {
          log('[detect.runAll:' + def.id + '] ' + (e && e.message || e));
        }
      };

      // Pass 1 — independents.
      for (const def of defs) {
        if (def.roi && def.roi.fromDetectionId) continue;
        runOne(def);
      }
      // Pass 2 — dependents (parent already in lastResults from pass 1).
      for (const def of defs) {
        if (!ranIds[def.id] && def.roi && def.roi.fromDetectionId) runOne(def);
      }
      return out;
    },

    /** Run an in-memory definition without persisting — used by the editor's live preview. */
    test(definition) { return host.runDetectionDefinition(definition); },
  };

  // ---------------- brickbot — event bus + actions + triggers + tick loop ----------------

  const __handlers = Object.create(null);     // eventName → [fn]
  const __actions = Object.create(null);      // actionName → fn
  const __triggers = [];                      // [{ predicate, action, cooldownMs, nextAllowed }]

  function __dispatch(eventName, payload) {
    const arr = __handlers[eventName];
    if (!arr) return;
    // Slice so off() during dispatch doesn't shift the loop.
    for (const fn of arr.slice()) {
      try { fn(payload); }
      catch (e) { host.log('[brickbot.on:' + eventName + '] ' + (e && e.stack || e)); }
    }
  }

  function __publishActions() {
    host.publishActions(Object.keys(__actions));
  }

  globalThis.brickbot = {
    /**
     * Subscribe a handler to a named event.
     * Built-in events: 'start', 'stop', 'tick', 'frame', 'error'.
     * Returns an unsubscribe function.
     */
    on(eventName, fn) {
      if (typeof fn !== 'function') throw new Error('brickbot.on: handler must be a function');
      const arr = __handlers[eventName] || (__handlers[eventName] = []);
      arr.push(fn);
      return function () { brickbot.off(eventName, fn); };
    },

    off(eventName, fn) {
      const arr = __handlers[eventName];
      if (!arr) return;
      const i = arr.indexOf(fn);
      if (i >= 0) arr.splice(i, 1);
    },

    emit(eventName, payload) { __dispatch(eventName, payload); },

    /** Register a named action — invokable from UI via the Actions panel or from
     *  scripts via brickbot.invoke(name). */
    action(name, fn) {
      if (typeof fn !== 'function') throw new Error('brickbot.action: handler must be a function');
      __actions[String(name)] = fn;
      __publishActions();
    },

    invoke(name) {
      const fn = __actions[String(name)];
      if (typeof fn !== 'function') {
        log('[brickbot.invoke] unknown action: ' + name);
        return;
      }
      try { fn(); }
      catch (e) { log('[brickbot.action:' + name + '] ' + (e && e.stack || e)); }
    },

    listActions() { return Object.keys(__actions); },

    /**
     * Declarative trigger. Predicate runs every tick; action fires when predicate is truthy.
     * @param {() => boolean} predicate
     * @param {() => void} action
     * @param {{ cooldownMs?: number }=} opts  cooldownMs throttles repeated firings (default 0).
     */
    when(predicate, action, opts) {
      __triggers.push({
        predicate: predicate,
        action: action,
        cooldownMs: (opts && opts.cooldownMs) || 0,
        nextAllowed: 0,
      });
    },

    /**
     * Main loop. Runs until isCancelled() (Stop pressed).
     * Each tick: drains queued action invocations, pumps a frame, fires 'frame' event,
     * runs trigger predicates, fires 'tick' event, sleeps tickMs.
     * Emits 'start' before the first tick and 'stop' on exit.
     */
    runForever(opts) {
      opts = opts || {};
      const tickMs = opts.tickMs != null ? opts.tickMs : 16;
      const autoDetect = !!opts.autoDetect;
      __dispatch('start', null);
      try {
        while (!isCancelled()) {
          // Pull any action invocations queued from UI / IPC.
          let pending;
          while ((pending = host.tryDequeueAction()) != null) {
            brickbot.invoke(pending);
          }

          // Pump a fresh frame so vision.* sees a consistent image during this tick.
          let pumped = null;
          try { pumped = host.pumpFrame(); }
          catch (e) { __dispatch('error', { phase: 'pump', message: String(e) }); }

          if (pumped !== null) __dispatch('frame', pumped);

          // Run all enabled detections — output bindings push into ctx + emit events
          // so triggers/handlers downstream see fresh state before they evaluate.
          if (autoDetect) {
            try { detect.runAll(); }
            catch (e) { __dispatch('error', { phase: 'detect', message: String(e) }); }
          }

          // Evaluate declarative triggers.
          const t = now();
          for (const tr of __triggers) {
            if (t < tr.nextAllowed) continue;
            let fired = false;
            try { fired = !!tr.predicate(); }
            catch (e) { __dispatch('error', { phase: 'trigger.predicate', message: String(e) }); }
            if (!fired) continue;
            try { tr.action(); tr.nextAllowed = t + tr.cooldownMs; }
            catch (e) { __dispatch('error', { phase: 'trigger.action', message: String(e) }); }
          }

          __dispatch('tick', null);
          wait(tickMs);
        }
      } finally {
        __dispatch('stop', null);
      }
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
