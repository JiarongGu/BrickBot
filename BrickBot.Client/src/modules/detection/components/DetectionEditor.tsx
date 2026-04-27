import React from 'react';
import { ColorPicker, Slider } from 'antd';
import type { Color as AntColor } from 'antd/es/color-picker';
import { useTranslation } from 'react-i18next';
import classNames from 'classnames';
import {
  CompactInput,
  CompactSegmented,
  CompactSelect,
  CompactSwitch,
} from '@/shared/components/compact';
import type {
  AnchorOrigin,
  BarOptions,
  ColorSpace,
  DetectionDefinition,
  DetectionKind,
  DetectionRoi,
  FillDirection,
  PatternOptions,
  RgbColor,
  TextOptions,
  TrackerAlgorithm,
  TrackerOptions,
} from '../types';
import { ANCHOR_ORIGINS, DETECTION_KIND_LABEL, newDetection } from '../types';

interface Props {
  draft: DetectionDefinition;
  /** All detections, used to populate the "ROI from another detection" + "anchor pattern" dropdowns. */
  siblingDetections: DetectionDefinition[];
  onChange: (next: DetectionDefinition) => void;
}

type RoiMode = 'manual' | 'anchored' | 'linked';

function roiMode(roi: DetectionRoi | undefined): RoiMode {
  if (!roi) return 'manual';
  if (roi.fromDetectionId) return 'linked';
  if (roi.anchor) return 'anchored';
  return 'manual';
}

/**
 * Right-pane editor for a {@link DetectionDefinition}. Four detection kinds — each with its
 * own simple form. The training wizard authors the heavy fields (init frame for tracker,
 * descriptors for pattern); this editor exposes the post-training tunables.
 */
export const DetectionEditor: React.FC<Props> = ({ draft, siblingDetections, onChange }) => {
  const { t } = useTranslation();

  const setKind = (kind: DetectionKind) => {
    if (kind === draft.kind) return;
    // Seed fresh per-kind defaults; keep the user-edited identity fields (name/group/etc).
    const fresh = newDetection(kind);
    onChange({
      ...fresh,
      id: draft.id,
      name: draft.name,
      group: draft.group,
      enabled: draft.enabled,
      roi: draft.roi,
      output: draft.output,
    });
  };

  return (
    <>
      {/* General */}
      <div className="detection-section">
        <div className="detection-section__title">{t('detection.section.general', 'General')}</div>
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.field.name', 'Name')}</span>
          <CompactInput
            value={draft.name}
            placeholder={t('detection.placeholder.name', 'e.g. HP Bar, Boss Sprite, Quest Banner') as string}
            onChange={(e) => onChange({ ...draft, name: e.target.value })}
          />
        </div>
        <div className="detection-row-2col">
          <div className="detection-field">
            <span className="detection-field__label">{t('detection.field.group', 'Group')}</span>
            <CompactInput
              value={draft.group ?? ''}
              placeholder={t('detection.placeholder.group', 'optional') as string}
              onChange={(e) => onChange({ ...draft, group: e.target.value || undefined })}
            />
          </div>
          <div className="detection-field">
            <span className="detection-field__label">{t('detection.field.enabled', 'Enabled')}</span>
            <CompactSwitch checked={draft.enabled} onChange={(c) => onChange({ ...draft, enabled: c })} />
          </div>
        </div>
      </div>

      {/* Kind */}
      <div className="detection-section">
        <div className="detection-section__title">{t('detection.section.kind', 'Kind')}</div>
        <CompactSelect
          value={draft.kind}
          onChange={(v) => setKind(v as DetectionKind)}
          options={(Object.keys(DETECTION_KIND_LABEL) as DetectionKind[]).map((k) => ({
            value: k,
            label: t(`detection.kind.${k}`, DETECTION_KIND_LABEL[k]),
          }))}
          style={{ width: '100%' }}
        />
      </div>

      {/* ROI */}
      <RoiSection draft={draft} siblings={siblingDetections} onChange={onChange} />

      {/* Per-kind */}
      {draft.kind === 'tracker' && draft.tracker && (
        <TrackerForm opt={draft.tracker} onChange={(opt) => onChange({ ...draft, tracker: opt })} />
      )}
      {draft.kind === 'pattern' && draft.pattern && (
        <PatternForm opt={draft.pattern} onChange={(opt) => onChange({ ...draft, pattern: opt })} />
      )}
      {draft.kind === 'text' && draft.text && (
        <TextForm opt={draft.text} onChange={(opt) => onChange({ ...draft, text: opt })} />
      )}
      {draft.kind === 'bar' && draft.bar && (
        <BarForm opt={draft.bar} siblings={siblingDetections} onChange={(opt) => onChange({ ...draft, bar: opt })} />
      )}

      <OutputSection draft={draft} onChange={onChange} />

      {/* Script usage hint */}
      <div className="detection-section">
        <div className="detection-section__title">{t('detection.section.usage', 'Use in scripts')}</div>
        <pre className="detection-script-hint">{`const r = detect.run('${draft.name || 'detection-name'}');\n// ${describeReturnShape(draft.kind)}`}</pre>
      </div>
    </>
  );
};

function describeReturnShape(kind: DetectionKind): string {
  switch (kind) {
    case 'tracker': return 'r.match = current bbox {x,y,w,h,cx,cy} when tracker is locked';
    case 'pattern': return 'r.match = projected bbox; r.confidence = match-ratio (0..1)';
    case 'text':    return 'r.text = recognized string; r.confidence = OCR confidence (0..1)';
    case 'bar':     return 'r.value = fill ratio (0..1); r.match = bar bbox; r.strip = sample band';
  }
}

// ============================================================================
//  Output: type + stability
// ============================================================================

const OutputSection: React.FC<{ draft: DetectionDefinition; onChange: (d: DetectionDefinition) => void }> = ({ draft, onChange }) => {
  const { t } = useTranslation();
  const out = draft.output ?? { eventOnChangeOnly: true };
  const setOutput = (patch: Partial<typeof out>) => onChange({ ...draft, output: { ...out, ...patch } });

  const defaultType: Record<DetectionKind, 'boolean' | 'number' | 'text' | 'bbox' | 'point'> = {
    tracker: 'bbox',
    pattern: 'bbox',
    text: 'text',
    bar: 'number',
  };
  const currentType = out.type ?? defaultType[draft.kind];
  const stab = out.stability ?? { minDurationMs: 0, tolerance: 0 };

  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.output', 'Output')}</div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.output.typeLabel', 'Output type')}</span>
        <CompactSelect
          value={currentType}
          onChange={(v) => setOutput({ type: v as typeof currentType })}
          options={[
            { value: 'boolean', label: t('detection.output.type.boolean', 'Boolean (found / not-found)') },
            { value: 'number',  label: t('detection.output.type.number', 'Number (fill / confidence)') },
            { value: 'text',    label: t('detection.output.type.text', 'Text (OCR)') },
            { value: 'bbox',    label: t('detection.output.type.bbox', 'Bounding box') },
            { value: 'point',   label: t('detection.output.type.point', 'Point (cx, cy)') },
          ]}
          style={{ width: '100%' }}
        />
      </div>

      <div className="detection-subtitle">{t('detection.output.stability', 'Stability filter')}</div>
      <div className="detection-section__hint">
        {t('detection.output.stabilityHint', 'Only return a value once it has been stable for this many milliseconds — filters single-frame flicker. 0 = disabled.')}
      </div>
      <div className="detection-row-2col">
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.output.minDuration', 'Min duration (ms)')}</span>
          <CompactInput
            size="small"
            value={stab.minDurationMs}
            onChange={(e) => setOutput({ stability: { minDurationMs: Math.max(0, Number(e.target.value) | 0), tolerance: stab.tolerance } })}
          />
        </div>
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.output.tolerance', 'Numeric tolerance')}</span>
          <CompactInput
            size="small"
            value={stab.tolerance}
            onChange={(e) => setOutput({ stability: { minDurationMs: stab.minDurationMs, tolerance: Math.max(0, parseFloat(e.target.value) || 0) } })}
          />
        </div>
      </div>
    </div>
  );
};

// ============================================================================
//  ROI section (manual / anchored / linked)
// ============================================================================

const RoiSection: React.FC<{
  draft: DetectionDefinition;
  siblings: DetectionDefinition[];
  onChange: (d: DetectionDefinition) => void;
}> = ({ draft, siblings, onChange }) => {
  const { t } = useTranslation();
  const mode = roiMode(draft.roi);

  const setMode = (m: RoiMode) => {
    if (m === 'manual') onChange({ ...draft, roi: draft.roi ? { x: draft.roi.x, y: draft.roi.y, w: draft.roi.w, h: draft.roi.h } : undefined });
    else if (m === 'anchored') onChange({ ...draft, roi: { x: 0, y: 0, w: draft.roi?.w ?? 200, h: draft.roi?.h ?? 50, anchor: draft.roi?.anchor ?? 'topLeft' } });
    else onChange({ ...draft, roi: { x: 0, y: 0, w: 0, h: 0, fromDetectionId: draft.roi?.fromDetectionId ?? siblings[0]?.id ?? '' } });
  };

  const linkable = siblings.filter((s) => s.id && s.id !== draft.id);

  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.roi', 'Region of interest')}</div>
      <CompactSegmented
        value={mode}
        onChange={(v) => setMode(v as RoiMode)}
        options={[
          { value: 'manual', label: t('detection.roi.manual', 'Manual') },
          { value: 'anchored', label: t('detection.roi.anchored', 'Anchored') },
          { value: 'linked', label: t('detection.roi.linked', 'Linked'), disabled: linkable.length === 0 },
        ]}
      />
      {mode === 'manual' && <RoiManual draft={draft} onChange={onChange} />}
      {mode === 'anchored' && <RoiAnchored draft={draft} onChange={onChange} />}
      {mode === 'linked' && <RoiLinked draft={draft} siblings={linkable} onChange={onChange} />}
    </div>
  );
};

const RoiManual: React.FC<{ draft: DetectionDefinition; onChange: (d: DetectionDefinition) => void }> = ({ draft, onChange }) => {
  const { t } = useTranslation();
  const roi = draft.roi ?? { x: 0, y: 0, w: 0, h: 0 };
  const set = (k: 'x' | 'y' | 'w' | 'h', v: number) => onChange({ ...draft, roi: { ...roi, [k]: Math.max(0, v | 0) } });
  return (
    <>
      <div className="detection-row-2col" style={{ marginTop: 4 }}>
        <CompactInput size="small" addonBefore="x" value={roi.x} onChange={(e) => set('x', Number(e.target.value))} />
        <CompactInput size="small" addonBefore="y" value={roi.y} onChange={(e) => set('y', Number(e.target.value))} />
      </div>
      <div className="detection-row-2col" style={{ marginTop: 4 }}>
        <CompactInput size="small" addonBefore="w" value={roi.w} onChange={(e) => set('w', Number(e.target.value))} />
        <CompactInput size="small" addonBefore="h" value={roi.h} onChange={(e) => set('h', Number(e.target.value))} />
      </div>
      {draft.roi && (
        <a onClick={() => onChange({ ...draft, roi: undefined })} style={{ display: 'inline-block', marginTop: 6, fontSize: 12 }}>
          {t('detection.roi.clear', 'clear ROI')}
        </a>
      )}
    </>
  );
};

const RoiAnchored: React.FC<{ draft: DetectionDefinition; onChange: (d: DetectionDefinition) => void }> = ({ draft, onChange }) => {
  const { t } = useTranslation();
  const roi = draft.roi ?? { x: 0, y: 0, w: 200, h: 50, anchor: 'topLeft' as AnchorOrigin };
  const set = (patch: Partial<DetectionRoi>) => onChange({ ...draft, roi: { ...roi, ...patch } });
  return (
    <>
      <div className="detection-subtitle">{t('detection.roi.anchor', 'Anchor')}</div>
      <AnchorGridPicker value={roi.anchor ?? 'topLeft'} onChange={(a) => set({ anchor: a })} />
      <div className="detection-row-2col" style={{ marginTop: 4 }}>
        <CompactInput size="small" addonBefore="dx" value={roi.x} onChange={(e) => set({ x: Number(e.target.value) | 0 })} />
        <CompactInput size="small" addonBefore="dy" value={roi.y} onChange={(e) => set({ y: Number(e.target.value) | 0 })} />
      </div>
      <div className="detection-row-2col" style={{ marginTop: 4 }}>
        <CompactInput size="small" addonBefore="w" value={roi.w} onChange={(e) => set({ w: Math.max(0, Number(e.target.value) | 0) })} />
        <CompactInput size="small" addonBefore="h" value={roi.h} onChange={(e) => set({ h: Math.max(0, Number(e.target.value) | 0) })} />
      </div>
    </>
  );
};

const RoiLinked: React.FC<{ draft: DetectionDefinition; siblings: DetectionDefinition[]; onChange: (d: DetectionDefinition) => void }> = ({ draft, siblings, onChange }) => {
  const { t } = useTranslation();
  const roi = draft.roi ?? { x: 0, y: 0, w: 0, h: 0, fromDetectionId: siblings[0]?.id };
  const set = (patch: Partial<DetectionRoi>) => onChange({ ...draft, roi: { ...roi, ...patch } });
  return (
    <>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.roi.linkedParent', 'Parent detection')}</span>
        <CompactSelect
          value={roi.fromDetectionId || undefined}
          onChange={(v) => set({ fromDetectionId: v })}
          options={siblings.map((s) => ({ value: s.id, label: `${s.name || s.id} (${s.kind})` }))}
          style={{ width: '100%' }}
        />
      </div>
      <div className="detection-subtitle">{t('detection.roi.insets', 'Inset margins (px)')}</div>
      <div className="detection-row-2col">
        <CompactInput size="small" addonBefore="L" value={roi.x} onChange={(e) => set({ x: Math.max(0, Number(e.target.value) | 0) })} />
        <CompactInput size="small" addonBefore="T" value={roi.y} onChange={(e) => set({ y: Math.max(0, Number(e.target.value) | 0) })} />
      </div>
      <div className="detection-row-2col" style={{ marginTop: 4 }}>
        <CompactInput size="small" addonBefore="R" value={roi.w} onChange={(e) => set({ w: Math.max(0, Number(e.target.value) | 0) })} />
        <CompactInput size="small" addonBefore="B" value={roi.h} onChange={(e) => set({ h: Math.max(0, Number(e.target.value) | 0) })} />
      </div>
    </>
  );
};

const AnchorGridPicker: React.FC<{ value: AnchorOrigin; onChange: (a: AnchorOrigin) => void }> = ({ value, onChange }) => (
  <div className="detection-anchor-grid">
    {ANCHOR_ORIGINS.map((a) => (
      <button
        key={a}
        className={classNames('detection-anchor-grid__cell', { 'detection-anchor-grid__cell--active': a === value })}
        onClick={() => onChange(a)}
        title={a}
        type="button"
      />
    ))}
  </div>
);

// ============================================================================
//  Per-kind editor forms
// ============================================================================

const TrackerForm: React.FC<{ opt: TrackerOptions; onChange: (o: TrackerOptions) => void }> = ({ opt, onChange }) => {
  const { t } = useTranslation();
  const algoLabels: Record<TrackerAlgorithm, string> = {
    kcf: t('detection.tracker.algo.kcf', 'KCF — fast (~150 fps), default') as string,
    csrt: t('detection.tracker.algo.csrt', 'CSRT — slow (~25 fps), most accurate') as string,
    mil: t('detection.tracker.algo.mil', 'MIL — robust to occlusions') as string,
  };
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.tracker', 'Tracker (moving element)')}</div>
      <div className="detection-section__hint">
        {t('detection.tracker.hint', 'OpenCV visual tracker. Initialized once on a chosen frame + bbox; subsequent runs follow the element. Re-train to change the init frame.')}
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.tracker.algo', 'Algorithm')}</span>
        <CompactSelect
          value={opt.algorithm}
          onChange={(v) => onChange({ ...opt, algorithm: v as TrackerAlgorithm })}
          options={(['kcf', 'csrt', 'mil'] as TrackerAlgorithm[]).map((a) => ({ value: a, label: algoLabels[a] }))}
          style={{ width: '100%' }}
        />
      </div>
      <div className="detection-field">
        <span className="detection-field__label">
          {t('detection.tracker.initBbox', 'Init bbox')}: ({opt.initX}, {opt.initY}) {opt.initW}×{opt.initH}
        </span>
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.tracker.reacquire', 'Re-acquire on lost')}</span>
        <CompactSwitch checked={opt.reacquireOnLost} onChange={(c) => onChange({ ...opt, reacquireOnLost: c })} />
      </div>
    </div>
  );
};

const PatternForm: React.FC<{ opt: PatternOptions; onChange: (o: PatternOptions) => void }> = ({ opt, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.pattern', 'Pattern (ORB descriptors)')}</div>
      <div className="detection-section__hint">
        {t('detection.pattern.hint', 'Background-invariant feature match. Trainer extracts ORB keypoints from positive samples; runtime matches them in the current frame. Re-train to refresh the model.')}
      </div>
      <div className="detection-field">
        <span className="detection-field__label">
          {t('detection.pattern.modelInfo', 'Model')}: {opt.keypointCount} keypoints, {opt.templateWidth}×{opt.templateHeight}
        </span>
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.pattern.minConfidence', 'Min confidence')}: {opt.minConfidence.toFixed(2)}</span>
        <Slider min={0.05} max={1.0} step={0.01} value={opt.minConfidence}
          onChange={(v) => onChange({ ...opt, minConfidence: v })} />
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.pattern.lowe', 'Lowe ratio')}: {opt.loweRatio.toFixed(2)}</span>
        <Slider min={0.5} max={0.9} step={0.01} value={opt.loweRatio}
          onChange={(v) => onChange({ ...opt, loweRatio: v })} />
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.pattern.maxKp', 'Max runtime keypoints')}: {opt.maxRuntimeKeypoints}</span>
        <Slider min={100} max={2000} step={50} value={opt.maxRuntimeKeypoints}
          onChange={(v) => onChange({ ...opt, maxRuntimeKeypoints: v })} />
      </div>
    </div>
  );
};

const TextForm: React.FC<{ opt: TextOptions; onChange: (o: TextOptions) => void }> = ({ opt, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.text', 'Text (OCR)')}</div>
      <div className="detection-section__hint">
        {t('detection.text.hint', 'Tesseract OCR. ROI defines what region to read. Tip: pre-binarize + upscale 2× for small game UI text.')}
      </div>
      <div className="detection-row-2col">
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.text.lang', 'Language')}</span>
          <CompactSelect
            value={opt.language}
            onChange={(v) => onChange({ ...opt, language: v as string })}
            options={[
              { value: 'eng', label: 'English (eng)' },
              { value: 'chi_sim', label: 'Chinese Simplified (chi_sim)' },
              { value: 'chi_tra', label: 'Chinese Traditional (chi_tra)' },
              { value: 'jpn', label: 'Japanese (jpn)' },
              { value: 'kor', label: 'Korean (kor)' },
            ]}
            style={{ width: '100%' }}
          />
        </div>
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.text.psm', 'Page seg mode')}</span>
          <CompactSelect
            value={opt.pageSegMode}
            onChange={(v) => onChange({ ...opt, pageSegMode: v as number })}
            options={[
              { value: 6, label: '6 — uniform block' },
              { value: 7, label: '7 — single line' },
              { value: 8, label: '8 — single word' },
              { value: 13, label: '13 — raw line, no layout' },
            ]}
            style={{ width: '100%' }}
          />
        </div>
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.text.regex', 'Match regex (optional)')}</span>
        <CompactInput
          value={opt.matchRegex ?? ''}
          placeholder={t('detection.text.regexPlaceholder', 'e.g. ^HP:\\s*\\d+/\\d+$') as string}
          onChange={(e) => onChange({ ...opt, matchRegex: e.target.value || undefined })}
        />
      </div>
      <div className="detection-row-2col">
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.text.binarize', 'Binarize')}</span>
          <CompactSwitch checked={opt.binarize} onChange={(c) => onChange({ ...opt, binarize: c })} />
        </div>
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.text.upscale', 'Upscale')}: {opt.upscaleFactor.toFixed(1)}×</span>
          <Slider min={1.0} max={4.0} step={0.5} value={opt.upscaleFactor}
            onChange={(v) => onChange({ ...opt, upscaleFactor: v })} />
        </div>
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.text.minConfidence', 'Min confidence (0..100)')}: {opt.minConfidence}</span>
        <Slider min={0} max={100} step={5} value={opt.minConfidence}
          onChange={(v) => onChange({ ...opt, minConfidence: v })} />
      </div>
    </div>
  );
};

const BarForm: React.FC<{ opt: BarOptions; siblings: DetectionDefinition[]; onChange: (o: BarOptions) => void }> = ({ opt, siblings, onChange }) => {
  const { t } = useTranslation();
  const setColor = (c: AntColor | string) => {
    const rgb = typeof c === 'string' ? hexToRgb(c) : { r: c.toRgb().r, g: c.toRgb().g, b: c.toRgb().b };
    onChange({ ...opt, fillColor: rgb });
  };
  const patterns = siblings.filter((s) => s.kind === 'pattern' && s.id);
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.bar', 'Bar (HP / MP / cooldown)')}</div>
      <div className="detection-section__hint">
        {t('detection.bar.hint', 'Bar locator: anchor on a Pattern detection or use ROI directly. Then samples a strip in the fill direction to compute fill ratio.')}
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.bar.anchor', 'Anchor pattern (optional)')}</span>
        <CompactSelect
          value={opt.anchorPatternId ?? ''}
          onChange={(v) => onChange({ ...opt, anchorPatternId: (v as string) || undefined })}
          options={[{ value: '', label: t('detection.bar.useRoi', '— None (use ROI directly) —') as string },
            ...patterns.map((p) => ({ value: p.id, label: p.name || p.id }))]}
          style={{ width: '100%' }}
        />
      </div>
      <div className="detection-row-2col">
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.bar.fillColor', 'Fill color')}</span>
          <ColorPicker value={rgbToHex(opt.fillColor)} onChange={(c) => setColor(c)} showText />
        </div>
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.bar.tolerance', 'Tolerance')}: ±{opt.tolerance}</span>
          <Slider min={5} max={120} step={5} value={opt.tolerance}
            onChange={(v) => onChange({ ...opt, tolerance: v })} />
        </div>
      </div>
      <div className="detection-row-2col">
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.bar.colorSpace', 'Color space')}</span>
          <CompactSegmented
            value={opt.colorSpace}
            onChange={(v) => onChange({ ...opt, colorSpace: v as ColorSpace })}
            options={[{ value: 'rgb', label: 'RGB' }, { value: 'hsv', label: 'HSV' }]}
          />
        </div>
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.bar.direction', 'Fill direction')}</span>
          <CompactSelect
            value={opt.direction}
            onChange={(v) => onChange({ ...opt, direction: v as FillDirection })}
            options={[
              { value: 'leftToRight', label: '→ Left to Right' },
              { value: 'rightToLeft', label: '← Right to Left' },
              { value: 'topToBottom', label: '↓ Top to Bottom' },
              { value: 'bottomToTop', label: '↑ Bottom to Top' },
            ]}
            style={{ width: '100%' }}
          />
        </div>
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.bar.lineThreshold', 'Line threshold')}: {opt.lineThreshold.toFixed(2)}</span>
        <Slider min={0.10} max={0.95} step={0.05} value={opt.lineThreshold}
          onChange={(v) => onChange({ ...opt, lineThreshold: v })} />
      </div>
      <div className="detection-row-2col">
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.bar.insetLeft', 'Inset (empty side) %')}: {(opt.insetLeftPct * 100).toFixed(0)}%</span>
          <Slider min={0} max={50} step={1} value={Math.round(opt.insetLeftPct * 100)}
            onChange={(v) => onChange({ ...opt, insetLeftPct: v / 100 })} />
        </div>
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.bar.insetRight', 'Inset (full side) %')}: {(opt.insetRightPct * 100).toFixed(0)}%</span>
          <Slider min={0} max={50} step={1} value={Math.round(opt.insetRightPct * 100)}
            onChange={(v) => onChange({ ...opt, insetRightPct: v / 100 })} />
        </div>
      </div>
    </div>
  );
};

// ============================================================================
//  Helpers
// ============================================================================

function rgbToHex(c: RgbColor): string {
  const h = (n: number) => n.toString(16).padStart(2, '0');
  return `#${h(c.r)}${h(c.g)}${h(c.b)}`;
}
function hexToRgb(hex: string): RgbColor {
  const s = hex.startsWith('#') ? hex.slice(1) : hex;
  return { r: parseInt(s.slice(0, 2), 16), g: parseInt(s.slice(2, 4), 16), b: parseInt(s.slice(4, 6), 16) };
}
