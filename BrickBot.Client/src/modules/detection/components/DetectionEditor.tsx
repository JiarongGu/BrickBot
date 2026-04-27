import React from 'react';
import { ColorPicker, Slider, Tooltip } from 'antd';
import type { Color as AntColor } from 'antd/es/color-picker';
import { useTranslation } from 'react-i18next';
import classNames from 'classnames';
import {
  CompactInput,
  CompactSegmented,
  CompactSelect,
  CompactSwitch,
} from '@/shared/components/compact';
import type { TemplateInfo } from '@/modules/template';
import type {
  AnchorOrigin,
  ColorPresenceOptions,
  ColorSpace,
  DetectionDefinition,
  DetectionKind,
  DetectionRoi,
  EffectOptions,
  FeatureMatchOptions,
  FillDirection,
  ProgressBarOptions,
  RegionOptions,
  RgbColor,
  TemplateOptions,
} from '../types';
import { ANCHOR_ORIGINS, DETECTION_KIND_LABEL } from '../types';

interface Props {
  draft: DetectionDefinition;
  /** All detections, used to populate the "ROI from another detection" dropdown. */
  siblingDetections: DetectionDefinition[];
  templates: TemplateInfo[];
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
 * Right-pane editor — name + kind picker + ROI section (manual / anchored / linked) +
 * per-kind options + output bindings.
 *
 * The big change vs. the older editor: ROI is now a tabbed section so users can choose
 * between absolute pixels, anchor-relative (top-right HUD that moves with window resize),
 * or linked (use another detection's match as the ROI). All ROI modes are persisted into
 * the same DetectionRoi shape — the runner picks the right resolver at run time.
 */
export const DetectionEditor: React.FC<Props> = ({ draft, siblingDetections, templates, onChange }) => {
  const { t } = useTranslation();

  const setKind = (kind: DetectionKind) => {
    const next: DetectionDefinition = { ...draft, kind };
    if (kind !== 'template') next.template = undefined;
    if (kind !== 'progressBar') next.progressBar = undefined;
    if (kind !== 'colorPresence') next.colorPresence = undefined;
    if (kind !== 'effect') next.effect = undefined;
    if (kind !== 'featureMatch') next.featureMatch = undefined;
    if (kind !== 'region') next.region = undefined;
    if (kind === 'template' && !draft.template) {
      next.template = { templateName: '', minConfidence: 0.85, scale: 1.0, grayscale: true, pyramid: false, edge: false };
    } else if (kind === 'progressBar' && !draft.progressBar) {
      next.progressBar = {
        templateName: '', minConfidence: 0.80, templateEdge: true,
        scale: 1.0, grayscale: true, pyramid: false,
        fillColor: { r: 220, g: 30, b: 30 },
        tolerance: 60, colorSpace: 'rgb',
        direction: 'leftToRight', lineThreshold: 0.4,
        insetLeftPct: 0.30, insetRightPct: 0.18,
      };
    } else if (kind === 'colorPresence' && !draft.colorPresence) {
      next.colorPresence = { color: { r: 220, g: 30, b: 30 }, tolerance: 30, minArea: 100, maxResults: 8, colorSpace: 'rgb' };
    } else if (kind === 'effect' && !draft.effect) {
      next.effect = { threshold: 0.15, autoBaseline: true, edge: false };
    } else if (kind === 'featureMatch' && !draft.featureMatch) {
      next.featureMatch = { templateName: '', minConfidence: 0.80, scaleMin: 0.9, scaleMax: 1.1, scaleSteps: 3, grayscale: true, edge: false };
    } else if (kind === 'region' && !draft.region) {
      next.region = {};
    }
    onChange(next);
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
            placeholder={t('detection.placeholder.name', 'e.g. HP Bar, Buff Up, Boss Spawn') as string}
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
      {draft.kind === 'template' && draft.template && (
        <TemplateForm opt={draft.template} templates={templates} onChange={(opt) => onChange({ ...draft, template: opt })} />
      )}
      {draft.kind === 'progressBar' && draft.progressBar && (
        <ProgressBarForm opt={draft.progressBar} templates={templates} onChange={(opt) => onChange({ ...draft, progressBar: opt })} />
      )}
      {draft.kind === 'colorPresence' && draft.colorPresence && (
        <ColorPresenceForm opt={draft.colorPresence} onChange={(opt) => onChange({ ...draft, colorPresence: opt })} />
      )}
      {draft.kind === 'effect' && draft.effect && (
        <EffectForm opt={draft.effect} onChange={(opt) => onChange({ ...draft, effect: opt })} />
      )}
      {draft.kind === 'featureMatch' && draft.featureMatch && (
        <FeatureMatchForm opt={draft.featureMatch} templates={templates} onChange={(opt) => onChange({ ...draft, featureMatch: opt })} />
      )}
      {draft.kind === 'region' && draft.region && (
        <RegionForm opt={draft.region} onChange={(opt) => onChange({ ...draft, region: opt })} />
      )}

      <OutputSection draft={draft} onChange={onChange} />

      {/* Script-usage hint */}
      <div className="detection-section">
        <div className="detection-section__title">{t('detection.section.usage', 'Use in scripts')}</div>
        <div className="detection-section__hint">
          {t('detection.usage.hint', 'Scripts read this detection by name. Wire ctx / events yourself in your script:')}
        </div>
        <pre className="detection-script-hint">{`const r = detect.run('${draft.name || 'detection-name'}');\n// r.typedValue is shaped per output.type (boolean/number/text/bbox/bboxes/point)\n// r.found, r.match, r.value still available for raw access\nctx.set('${draft.name || 'value'}', r.typedValue);`}</pre>
      </div>
    </>
  );
};

// ============= Output: type + stability =============

/** Output configuration: what shape `r.typedValue` takes + optional debounce. Replaces the
 *  old ctx/event/overlay UI (scripts handle those themselves now). */
const OutputSection: React.FC<{ draft: DetectionDefinition; onChange: (d: DetectionDefinition) => void }> = ({ draft, onChange }) => {
  const { t } = useTranslation();
  const out = draft.output ?? { eventOnChangeOnly: true };
  const setOutput = (patch: Partial<typeof out>) => onChange({ ...draft, output: { ...out, ...patch } });

  // Default per-kind output type when user hasn't picked one.
  const defaultType: Record<DetectionKind, 'boolean' | 'number' | 'bbox' | 'bboxes'> = {
    template: 'bbox',
    progressBar: 'number',
    colorPresence: 'bboxes',
    effect: 'boolean',
    featureMatch: 'bbox',
    region: 'bbox',
  };
  const currentType = out.type ?? defaultType[draft.kind];
  const stab = out.stability ?? { minDurationMs: 0, tolerance: 0 };

  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.output', 'Output')}</div>
      <div className="detection-section__hint">
        {t('detection.output.typeHint', 'Picks the shape returned by detect.run(name).typedValue.')}
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.output.typeLabel', 'Output type')}</span>
        <CompactSelect
          value={currentType}
          onChange={(v) => setOutput({ type: v as 'boolean' | 'number' | 'text' | 'bbox' | 'bboxes' | 'point' })}
          options={[
            { value: 'boolean', label: t('detection.output.type.boolean', 'Boolean (found / not-found)') },
            { value: 'number',  label: t('detection.output.type.number', 'Number (fill / count / confidence)') },
            { value: 'text',    label: t('detection.output.type.text', 'Text (OCR / label)') },
            { value: 'bbox',    label: t('detection.output.type.bbox', 'Bounding box (single)') },
            { value: 'bboxes',  label: t('detection.output.type.bboxes', 'Bounding boxes (list)') },
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

// ============= ROI section =============

const RoiSection: React.FC<{ draft: DetectionDefinition; siblings: DetectionDefinition[]; onChange: (d: DetectionDefinition) => void }> = ({
  draft, siblings, onChange,
}) => {
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
      <div className="detection-section__hint">{t('detection.roi.dragHint', 'Drag on the canvas to set, or type pixels:')}</div>
      <div className="detection-row-2col">
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
      <div className="detection-section__hint">
        {t('detection.roi.anchorHint', 'Coords are offsets from this corner. Right/bottom anchors take negative offsets to move inward.')}
      </div>
      <div className="detection-subtitle">{t('detection.roi.offset', 'Offset')}</div>
      <div className="detection-row-2col">
        <CompactInput size="small" addonBefore="dx" value={roi.x} onChange={(e) => set({ x: Number(e.target.value) | 0 })} />
        <CompactInput size="small" addonBefore="dy" value={roi.y} onChange={(e) => set({ y: Number(e.target.value) | 0 })} />
      </div>
      <div className="detection-subtitle">{t('detection.roi.size', 'Size')}</div>
      <div className="detection-row-2col">
        <CompactInput size="small" addonBefore="w" value={roi.w} onChange={(e) => set({ w: Math.max(0, Number(e.target.value) | 0) })} />
        <CompactInput size="small" addonBefore="h" value={roi.h} onChange={(e) => set({ h: Math.max(0, Number(e.target.value) | 0) })} />
      </div>
    </>
  );
};

const RoiLinked: React.FC<{ draft: DetectionDefinition; siblings: DetectionDefinition[]; onChange: (d: DetectionDefinition) => void }> = ({
  draft, siblings, onChange,
}) => {
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
      <div className="detection-section__hint">
        {t('detection.roi.linkedHint', 'Uses the parent\'s match bbox as this ROI. Inset margins shrink it inward.')}
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

// ============= 3×3 anchor grid picker =============

const AnchorGridPicker: React.FC<{ value: AnchorOrigin; onChange: (a: AnchorOrigin) => void }> = ({ value, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="anchor-grid">
      {ANCHOR_ORIGINS.map((a) => (
        <Tooltip key={a} title={t(`detection.anchor.${a}`, a)}>
          <div
            className={classNames('anchor-grid__cell', { 'anchor-grid__cell--active': value === a })}
            onClick={() => onChange(a)}
          >
            ●
          </div>
        </Tooltip>
      ))}
    </div>
  );
};

// ============= Per-kind sub-forms =============

const TemplateForm: React.FC<{ opt: TemplateOptions; templates: TemplateInfo[]; onChange: (o: TemplateOptions) => void }> = ({
  opt, templates, onChange,
}) => {
  const { t } = useTranslation();
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.template', 'Template options')}</div>
      <TemplatePickerField
        value={opt.templateName}
        templates={templates}
        onChange={(v) => onChange({ ...opt, templateName: v })}
      />
      <ConfidenceSlider value={opt.minConfidence} onChange={(v) => onChange({ ...opt, minConfidence: v })} />
      <ScaleSlider value={opt.scale} onChange={(v) => onChange({ ...opt, scale: v })} />
      <MatchModeSegmented edge={opt.edge} grayscale={opt.grayscale}
        onChange={(g, e) => onChange({ ...opt, grayscale: g, edge: e })} />
      <div className="detection-field">
        <label className="detection-inline-label">
          <input type="checkbox" checked={opt.pyramid} onChange={(e) => onChange({ ...opt, pyramid: e.target.checked })} />
          {t('detection.field.pyramid', 'Pyramid coarse-to-fine (~5× faster)')}
        </label>
      </div>
    </div>
  );
};

const ProgressBarForm: React.FC<{ opt: ProgressBarOptions; templates: TemplateInfo[]; onChange: (o: ProgressBarOptions) => void }> = ({
  opt, templates, onChange,
}) => {
  const { t } = useTranslation();
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.bar', 'Progress bar options')}</div>

      <div className="detection-subtitle">{t('detection.bar.bbox', 'Bar bbox')}</div>
      <div className="detection-section__hint">
        {t('detection.bar.bboxHint', 'Either select a template that frames the bar, or leave the template blank and use the ROI section above to anchor / link the bbox.')}
      </div>
      <TemplatePickerField
        value={opt.templateName}
        templates={templates}
        onChange={(v) => onChange({ ...opt, templateName: v })}
        allowEmpty
      />
      {opt.templateName && (
        <>
          <ConfidenceSlider value={opt.minConfidence} onChange={(v) => onChange({ ...opt, minConfidence: v })} />
          <ScaleSlider value={opt.scale} onChange={(v) => onChange({ ...opt, scale: v })} />
          <MatchModeSegmented edge={opt.templateEdge} grayscale={opt.grayscale}
            onChange={(g, e) => onChange({ ...opt, grayscale: g, templateEdge: e })} />
          <div className="detection-field">
            <label className="detection-inline-label">
              <input type="checkbox" checked={opt.pyramid} onChange={(e) => onChange({ ...opt, pyramid: e.target.checked })} />
              {t('detection.field.pyramid', 'Pyramid coarse-to-fine (~5× faster)')}
            </label>
          </div>
        </>
      )}

      <div className="detection-subtitle">{t('detection.bar.fill', 'Fill measurement')}</div>
      <ColorRow label={t('detection.field.fillColor', 'Fill color')} color={opt.fillColor}
        onChange={(c) => onChange({ ...opt, fillColor: c })} />
      <ColorSpaceRow value={opt.colorSpace} onChange={(cs) => onChange({ ...opt, colorSpace: cs })} />
      <ToleranceSlider value={opt.tolerance} colorSpace={opt.colorSpace}
        onChange={(v) => onChange({ ...opt, tolerance: v })} />
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.field.direction', 'Fill direction')}</span>
        <FillDirectionPicker value={opt.direction} onChange={(d) => onChange({ ...opt, direction: d })} />
      </div>
      <div className="detection-field">
        <span className="detection-field__label">
          {t('detection.field.lineThreshold', 'Line fill threshold')}: {(opt.lineThreshold * 100).toFixed(0)}%
        </span>
        <Slider min={0.1} max={0.95} step={0.05} value={opt.lineThreshold}
          onChange={(v) => onChange({ ...opt, lineThreshold: v })} />
        <div className="detection-section__hint">
          {t('detection.field.lineThresholdHint', 'Fraction of pixels per orthogonal line that must match the fill color for that line to count as filled.')}
        </div>
      </div>
      <div className="detection-row-2col">
        <div className="detection-field">
          <span className="detection-field__label">
            {t('detection.field.insetLeft', 'Inset start %')}: {(opt.insetLeftPct * 100).toFixed(0)}
          </span>
          <Slider min={0} max={0.5} step={0.02} value={opt.insetLeftPct}
            onChange={(v) => onChange({ ...opt, insetLeftPct: v })} />
        </div>
        <div className="detection-field">
          <span className="detection-field__label">
            {t('detection.field.insetRight', 'Inset end %')}: {(opt.insetRightPct * 100).toFixed(0)}
          </span>
          <Slider min={0} max={0.5} step={0.02} value={opt.insetRightPct}
            onChange={(v) => onChange({ ...opt, insetRightPct: v })} />
        </div>
      </div>
    </div>
  );
};

const ColorPresenceForm: React.FC<{ opt: ColorPresenceOptions; onChange: (o: ColorPresenceOptions) => void }> = ({ opt, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.colorPresence', 'Color presence options')}</div>
      <ColorRow label={t('detection.field.targetColor', 'Target color')} color={opt.color}
        onChange={(c) => onChange({ ...opt, color: c })} />
      <ColorSpaceRow value={opt.colorSpace} onChange={(cs) => onChange({ ...opt, colorSpace: cs })} />
      <ToleranceSlider value={opt.tolerance} colorSpace={opt.colorSpace}
        onChange={(v) => onChange({ ...opt, tolerance: v })} />
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.field.minArea', 'Min blob area (px²)')}: {opt.minArea}</span>
        <Slider min={1} max={5000} step={10} value={opt.minArea}
          onChange={(v) => onChange({ ...opt, minArea: v })} />
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.field.maxResults', 'Max results')}: {opt.maxResults}</span>
        <Slider min={1} max={64} step={1} value={opt.maxResults}
          onChange={(v) => onChange({ ...opt, maxResults: v })} />
      </div>
    </div>
  );
};

const EffectForm: React.FC<{ opt: EffectOptions; onChange: (o: EffectOptions) => void }> = ({ opt, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.effect', 'Visual effect options')}</div>
      <div className="detection-field">
        <span className="detection-field__label">
          {t('detection.field.threshold', 'Trigger threshold')}: {opt.threshold.toFixed(2)}
        </span>
        <Slider min={0.01} max={1.0} step={0.01} value={opt.threshold}
          onChange={(v) => onChange({ ...opt, threshold: v })} />
      </div>
      <div className="detection-field">
        <label className="detection-inline-label">
          <input type="checkbox" checked={opt.autoBaseline}
            onChange={(e) => onChange({ ...opt, autoBaseline: e.target.checked })} />
          {t('detection.field.autoBaseline', 'Auto-snapshot baseline on first frame')}
        </label>
      </div>
      <div className="detection-field">
        <label className="detection-inline-label">
          <input type="checkbox" checked={opt.edge}
            onChange={(e) => onChange({ ...opt, edge: e.target.checked })} />
          {t('detection.field.edgeDiff', 'Edge diff (ignores color/lighting drift, only flags shape changes)')}
        </label>
      </div>
    </div>
  );
};

const FeatureMatchForm: React.FC<{ opt: FeatureMatchOptions; templates: TemplateInfo[]; onChange: (o: FeatureMatchOptions) => void }> = ({
  opt, templates, onChange,
}) => {
  const { t } = useTranslation();
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.feature', 'Feature match (scale-invariant)')}</div>
      <TemplatePickerField value={opt.templateName} templates={templates}
        onChange={(v) => onChange({ ...opt, templateName: v })} />
      <ConfidenceSlider value={opt.minConfidence} onChange={(v) => onChange({ ...opt, minConfidence: v })} />
      <MatchModeSegmented edge={opt.edge} grayscale={opt.grayscale}
        onChange={(g, e) => onChange({ ...opt, grayscale: g, edge: e })} />
      <div className="detection-row-2col">
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.field.scaleMin', 'Scale min')}: {opt.scaleMin.toFixed(2)}</span>
          <Slider min={0.25} max={1.0} step={0.05} value={opt.scaleMin}
            onChange={(v) => onChange({ ...opt, scaleMin: v })} />
        </div>
        <div className="detection-field">
          <span className="detection-field__label">{t('detection.field.scaleMax', 'Scale max')}: {opt.scaleMax.toFixed(2)}</span>
          <Slider min={1.0} max={2.0} step={0.05} value={opt.scaleMax}
            onChange={(v) => onChange({ ...opt, scaleMax: v })} />
        </div>
      </div>
      <div className="detection-field">
        <span className="detection-field__label">{t('detection.field.scaleSteps', 'Scale steps')}: {opt.scaleSteps}</span>
        <Slider min={1} max={9} step={1} value={opt.scaleSteps}
          onChange={(v) => onChange({ ...opt, scaleSteps: v })} />
      </div>
    </div>
  );
};

const RegionForm: React.FC<{ opt: RegionOptions; onChange: (o: RegionOptions) => void }> = ({ opt, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-section">
      <div className="detection-section__title">{t('detection.section.region', 'Region (anchor)')}</div>
      <div className="detection-section__hint">
        {t('detection.region.hint', 'Region kind has no detection logic — its result IS the resolved ROI. Other detections can reference this one via the Linked ROI source.')}
      </div>
      <div className="detection-field" style={{ marginTop: 8 }}>
        <span className="detection-field__label">{t('detection.region.note', 'Note (optional)')}</span>
        <CompactInput
          value={opt.note ?? ''}
          placeholder={t('detection.region.notePlaceholder', 'Top-right HUD area, etc.') as string}
          onChange={(e) => onChange({ ...opt, note: e.target.value || undefined })}
        />
      </div>
    </div>
  );
};

// Output bindings UI removed — scripts call detect.run('name') and decide what to do with
// the result. Existing detections that have ctx / event / overlay bindings still work at
// runtime (StdLib detect.run honors def.output) but new detections leave them empty.

// ============= Shared widgets =============

const TemplatePickerField: React.FC<{
  value: string; templates: TemplateInfo[]; onChange: (v: string) => void; allowEmpty?: boolean;
}> = ({ value, templates, onChange, allowEmpty }) => {
  const { t } = useTranslation();
  const opts: { value: string; label: string }[] = templates.map((tpl) => ({
    value: tpl.id,
    label: tpl.description ? `${tpl.name} — ${tpl.description}` : tpl.name,
  }));
  if (allowEmpty) opts.unshift({ value: '', label: t('detection.template.none', '— No template (use ROI directly) —') });
  return (
    <div className="detection-field">
      <span className="detection-field__label">{t('detection.field.template', 'Template')}</span>
      <CompactSelect
        value={value || (allowEmpty ? '' : undefined)}
        onChange={(v) => onChange(v ?? '')}
        options={opts}
        placeholder={t('detection.field.template.placeholder', 'Pick a saved template')}
        style={{ width: '100%' }}
        disabled={templates.length === 0 && !allowEmpty}
      />
    </div>
  );
};

const ConfidenceSlider: React.FC<{ value: number; onChange: (v: number) => void }> = ({ value, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-field">
      <span className="detection-field__label">{t('detection.field.minConfidence', 'Min confidence')}: {value.toFixed(2)}</span>
      <Slider min={0.5} max={0.99} step={0.01} value={value} onChange={onChange} />
    </div>
  );
};

const ScaleSlider: React.FC<{ value: number; onChange: (v: number) => void }> = ({ value, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-field">
      <span className="detection-field__label">{t('detection.field.scale', 'Scale')}: {value.toFixed(2)}</span>
      <Slider min={0.25} max={1.0} step={0.05} value={value} onChange={onChange} />
    </div>
  );
};

const MatchModeSegmented: React.FC<{
  edge: boolean; grayscale: boolean; onChange: (grayscale: boolean, edge: boolean) => void;
}> = ({ edge, grayscale, onChange }) => {
  const { t } = useTranslation();
  const mode = edge ? 'edge' : grayscale ? 'grayscale' : 'color';
  return (
    <div className="detection-field">
      <span className="detection-field__label">{t('detection.field.matchMode', 'Match mode')}</span>
      <CompactSegmented
        value={mode}
        onChange={(v) => {
          const m = v as 'color' | 'grayscale' | 'edge';
          onChange(m !== 'color', m === 'edge');
        }}
        options={[
          { value: 'color', label: t('detection.matchMode.color', 'Color') },
          { value: 'grayscale', label: t('detection.matchMode.grayscale', 'Grayscale') },
          { value: 'edge', label: t('detection.matchMode.edge', 'Edge / Shape') },
        ]}
      />
      <div className="detection-section__hint">
        {edge
          ? t('detection.matchMode.edgeHint', 'Compares Canny edges only — survives color drift, lighting, variable fill.')
          : grayscale
            ? t('detection.matchMode.grayscaleHint', '~3× faster than color; some accuracy loss on color-discriminated UI.')
            : t('detection.matchMode.colorHint', 'Strict pixel match — fastest but breakable by color/light shifts.')}
      </div>
    </div>
  );
};

const ToleranceSlider: React.FC<{ value: number; colorSpace: ColorSpace; onChange: (v: number) => void }> = ({ value, colorSpace, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-field">
      <span className="detection-field__label">
        {colorSpace === 'hsv' ? t('detection.field.hueTolerance', 'Hue tolerance (°)') : t('detection.field.tolerance', 'Tolerance')}: ±{value}
      </span>
      <Slider min={0} max={colorSpace === 'hsv' ? 90 : 120} step={1} value={value} onChange={onChange} />
    </div>
  );
};

const FillDirectionPicker: React.FC<{ value: FillDirection; onChange: (d: FillDirection) => void }> = ({ value, onChange }) => {
  // 3×3 grid: top is up arrow, left is left arrow, etc. Center cell shows current selection.
  const { t } = useTranslation();
  const cell = (label: string, dir: FillDirection | undefined, sym: string) => (
    <Tooltip title={dir ? t(`detection.direction.${dir}`, label) : ''}>
      <div
        className={classNames('fill-direction__cell', {
          'fill-direction__cell--filler': !dir,
          'fill-direction__cell--active': dir && value === dir,
        })}
        onClick={() => dir && onChange(dir)}
      >
        {sym}
      </div>
    </Tooltip>
  );
  return (
    <div className="fill-direction">
      {cell('', undefined, '')}
      {cell('top to bottom', 'topToBottom', '↓')}
      {cell('', undefined, '')}
      {cell('left to right', 'leftToRight', '→')}
      {cell('', undefined, '·')}
      {cell('right to left', 'rightToLeft', '←')}
      {cell('', undefined, '')}
      {cell('bottom to top', 'bottomToTop', '↑')}
      {cell('', undefined, '')}
    </div>
  );
};

const ColorRow: React.FC<{ label: string; color: RgbColor; onChange: (c: RgbColor) => void }> = ({ label, color, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-field">
      <span className="detection-field__label">{label}</span>
      <div className="detection-color-row">
        <ColorPicker
          size="small"
          value={`rgb(${color.r}, ${color.g}, ${color.b})`}
          format="rgb"
          showText
          disabledAlpha
          onChange={(c: AntColor) => {
            const rgb = c.toRgb();
            onChange({ r: rgb.r, g: rgb.g, b: rgb.b });
          }}
        />
        <span className="detection-color-row__hint">
          {t('detection.field.color.altClickHint', 'alt+click canvas to pick')}
        </span>
      </div>
    </div>
  );
};

const ColorSpaceRow: React.FC<{ value: ColorSpace; onChange: (cs: ColorSpace) => void }> = ({ value, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="detection-field">
      <span className="detection-field__label">{t('detection.field.colorSpace', 'Color space')}</span>
      <CompactSegmented
        value={value}
        onChange={(v) => onChange(v as ColorSpace)}
        options={[
          { value: 'rgb', label: t('detection.colorSpace.rgb', 'RGB (exact)') },
          { value: 'hsv', label: t('detection.colorSpace.hsv', 'HSV (lighting-tolerant)') },
        ]}
      />
    </div>
  );
};
