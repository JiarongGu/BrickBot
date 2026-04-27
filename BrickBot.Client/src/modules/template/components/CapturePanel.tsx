import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ColorPicker, Empty, Form, List, Popconfirm, Slider, Tag, Tooltip, message } from 'antd';
import type { Color as AntColor } from 'antd/es/color-picker';
import {
  AimOutlined,
  BgColorsOutlined,
  BorderOutlined,
  CameraOutlined,
  DeleteOutlined,
  PercentageOutlined,
  PlayCircleOutlined,
  PushpinOutlined,
  ReloadOutlined,
  ScissorOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  CompactAlert,
  CompactButton,
  CompactCard,
  CompactDangerButton,
  CompactInput,
  CompactPrimaryButton,
  CompactSegmented,
  CompactSelect,
  CompactSpace,
} from '@/shared/components/compact';
import { FormDialog } from '@/shared/components/dialogs';
import { useProfileStore } from '@/modules/profile';
import { captureService } from '@/modules/runner/services/captureService';
import type { WindowInfo } from '@/modules/runner/types';
import { useEditorBridgeStore } from '@/modules/script/store/editorBridgeStore';
import {
  visionService,
  type BarRect,
  type ColorBlob,
  type DetectionMethod,
  type VisionMatch,
} from '@/modules/vision';
import { templateService } from '../services/templateService';
import type { CropRect, TemplateInfo } from '../types';
import './CapturePanel.css';

interface CaptureState {
  pngBase64: string;
  width: number;
  height: number;
}

interface HoverState { x: number; y: number; r: number; g: number; b: number; }

interface TemplateConfig { name?: string; minConfidence: number; scale: number; grayscale: boolean; }
interface ColorsConfig { color: { r: number; g: number; b: number }; tolerance: number; minArea: number; }
interface PercentBarConfig {
  templateName?: string;
  color: { r: number; g: number; b: number };
  tolerance: number;
  minConfidence: number;
}

type DetectionResult =
  | { kind: 'template'; match: VisionMatch | null; templateName: string; durationMs: number }
  | { kind: 'colors'; blobs: ColorBlob[]; durationMs: number }
  | { kind: 'percentBar'; bar: BarRect | null; strip: BarRect | null; fill: number; templateName: string; durationMs: number };

/**
 * Capture & Templates panel — pick a target window, grab one frame, hover for pixel
 * coordinates + RGB color, drag a rectangle to crop a region, save as a template PNG,
 * and live-test detection (template match / color blobs / bar fill %) against the frame
 * before committing the call to a script.
 *
 * Canvas interactions:
 *   - drag        → crop rectangle (used for save-template + percentBar ROI)
 *   - alt+click   → pick pixel color into the active detection config
 *   - hover       → x/y/RGB readout
 */
export const CapturePanel: React.FC = () => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);

  const [windows, setWindows] = useState<WindowInfo[]>([]);
  const [windowHandle, setWindowHandle] = useState<number | undefined>();
  const [capture, setCapture] = useState<CaptureState | undefined>();
  const [grabbing, setGrabbing] = useState(false);
  const [crop, setCrop] = useState<CropRect | undefined>();
  const [hover, setHover] = useState<HoverState | undefined>();
  const [templates, setTemplates] = useState<TemplateInfo[]>([]);
  const [saveModalOpen, setSaveModalOpen] = useState(false);
  const [saveForm] = Form.useForm();

  const [method, setMethod] = useState<DetectionMethod>('template');
  // Defaults bias toward "always finds it first" — grayscale (~3× free) but full scale.
  // Users can dial scale down once detection works for their template.
  const [tplConfig, setTplConfig] = useState<TemplateConfig>({ minConfidence: 0.80, scale: 1.0, grayscale: true });
  const [colorsConfig, setColorsConfig] = useState<ColorsConfig>({ color: { r: 220, g: 30, b: 30 }, tolerance: 30, minArea: 100 });
  const [barConfig, setBarConfig] = useState<PercentBarConfig>({
    color: { r: 220, g: 220, b: 220 }, tolerance: 60, minConfidence: 0.80,
  });
  const [result, setResult] = useState<DetectionResult | undefined>();
  const [detecting, setDetecting] = useState(false);

  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const dragStartRef = useRef<{ x: number; y: number } | null>(null);

  const refreshWindows = useCallback(async () => {
    const list = await captureService.listWindows();
    setWindows(list);
    if (list.length > 0 && !windowHandle) setWindowHandle(list[0].handle);
  }, [windowHandle]);

  const refreshTemplates = useCallback(async () => {
    if (!profileId) return;
    const { templates: list } = await templateService.list(profileId);
    setTemplates(list);
    if (!tplConfig.name && list.length > 0) setTplConfig((c) => ({ ...c, name: list[0].name }));
  }, [profileId, tplConfig.name]);

  useEffect(() => { void refreshWindows(); }, [refreshWindows]);
  useEffect(() => { void refreshTemplates(); }, [refreshTemplates]);

  const grab = useCallback(async () => {
    if (!windowHandle) {
      message.warning(t('capture.pickWindowFirst', 'Pick a window first.'));
      return;
    }
    setGrabbing(true);
    setCrop(undefined);
    setResult(undefined);
    try {
      const r = await captureService.grabPng(windowHandle);
      setCapture(r);
    } catch (err) {
      message.error(String(err));
    } finally {
      setGrabbing(false);
    }
  }, [windowHandle, t]);

  // ---------------- Detection runners (one per method) ----------------

  const runTemplate = useCallback(async () => {
    if (!profileId || !capture || !tplConfig.name) return;
    setDetecting(true);
    try {
      const r = await visionService.testTemplate(profileId, tplConfig.name, capture.pngBase64, {
        minConfidence: tplConfig.minConfidence,
        grayscale: tplConfig.grayscale,
        scale: tplConfig.scale,
      });
      setResult({ kind: 'template', match: r.match, templateName: tplConfig.name, durationMs: r.durationMs });
    } catch (err) { message.error(String(err)); }
    finally { setDetecting(false); }
  }, [profileId, capture, tplConfig]);

  const runColors = useCallback(async () => {
    if (!capture) return;
    setDetecting(true);
    try {
      const c = colorsConfig.color;
      const tol = colorsConfig.tolerance;
      const range = {
        rMin: Math.max(0, c.r - tol), rMax: Math.min(255, c.r + tol),
        gMin: Math.max(0, c.g - tol), gMax: Math.min(255, c.g + tol),
        bMin: Math.max(0, c.b - tol), bMax: Math.min(255, c.b + tol),
      };
      const r = await visionService.testFindColors(capture.pngBase64, range, { minArea: colorsConfig.minArea });
      setResult({ kind: 'colors', blobs: r.blobs, durationMs: r.durationMs });
    } catch (err) { message.error(String(err)); }
    finally { setDetecting(false); }
  }, [capture, colorsConfig]);

  const runPercentBar = useCallback(async () => {
    if (!profileId || !capture || !barConfig.templateName) {
      message.warning(t('capture.detect.bar.needTemplate', 'Pick a template that frames the bar.'));
      return;
    }
    setDetecting(true);
    try {
      const r = await visionService.testBarFromTemplate(
        profileId, barConfig.templateName, capture.pngBase64, barConfig.color,
        { tolerance: barConfig.tolerance, minConfidence: barConfig.minConfidence },
      );
      setResult({
        kind: 'percentBar', bar: r.bar, strip: r.strip, fill: r.fill,
        templateName: barConfig.templateName, durationMs: r.durationMs,
      });
    } catch (err) { message.error(String(err)); }
    finally { setDetecting(false); }
  }, [profileId, capture, barConfig, t]);

  const runActiveMethod = useCallback(() => {
    if (method === 'template') void runTemplate();
    else if (method === 'colors') void runColors();
    else if (method === 'percentBar') void runPercentBar();
  }, [method, runTemplate, runColors, runPercentBar]);

  // ---------------- Snippet inserts ----------------

  const insertSnippet = useCallback((text: string) => {
    const ok = useEditorBridgeStore.getState().insertAtCursor(text);
    if (!ok) message.warning(t('capture.openEditor', 'Open the Scripts editor to insert.'));
  }, [t]);

  const insertActiveResultSnippet = () => {
    if (!result) return;
    if (result.kind === 'template' && result.templateName) {
      const opts: string[] = [];
      if (tplConfig.minConfidence !== 0.85) opts.push(`minConfidence: ${tplConfig.minConfidence.toFixed(2)}`);
      if (tplConfig.scale !== 1.0) opts.push(`scale: ${tplConfig.scale}`);
      if (tplConfig.grayscale) opts.push(`grayscale: true`);
      const optsStr = opts.length > 0 ? `, { ${opts.join(', ')} }` : '';
      insertSnippet(`vision.find('${result.templateName}.png'${optsStr})`);
    } else if (result.kind === 'colors') {
      const c = colorsConfig.color;
      const tol = colorsConfig.tolerance;
      insertSnippet(
        `vision.findColors({ rMin: ${Math.max(0, c.r - tol)}, rMax: ${Math.min(255, c.r + tol)}, ` +
        `gMin: ${Math.max(0, c.g - tol)}, gMax: ${Math.min(255, c.g + tol)}, ` +
        `bMin: ${Math.max(0, c.b - tol)}, bMax: ${Math.min(255, c.b + tol)} }, ` +
        `{ minArea: ${colorsConfig.minArea} })`,
      );
    } else if (result.kind === 'percentBar') {
      const c = barConfig.color;
      // Snippet shows the chained "find bar then percent its strip" pattern.
      insertSnippet(
        `(() => {\n` +
        `  const bar = vision.find('${result.templateName}.png', { grayscale: true, minConfidence: ${barConfig.minConfidence.toFixed(2)} });\n` +
        `  if (!bar) return 0;\n` +
        `  // strip ROI auto-discovered from CapturePanel; tweak inset offsets as needed.\n` +
        `  const insetL = Math.floor(bar.w * 0.30), insetR = Math.floor(bar.w * 0.18);\n` +
        `  const stripY = bar.y + ${result.strip ? result.strip.y - (result.bar?.y ?? 0) : 'Math.floor(bar.h / 2)'};\n` +
        `  const strip = { x: bar.x + insetL, y: stripY, w: bar.w - insetL - insetR, h: 5 };\n` +
        `  return vision.percentBar(strip, { r: ${c.r}, g: ${c.g}, b: ${c.b} }, { tolerance: ${barConfig.tolerance} });\n` +
        `})()`,
      );
    }
  };

  // ---------------- Canvas rendering ----------------

  useEffect(() => {
    if (!capture || !canvasRef.current) return;
    const canvas = canvasRef.current;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const img = new Image();
    img.onload = () => {
      canvas.width = capture.width;
      canvas.height = capture.height;
      ctx.drawImage(img, 0, 0);
      drawOverlay();
    };
    img.src = `data:image/png;base64,${capture.pngBase64}`;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [capture]);

  const drawOverlay = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas || !capture) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    const img = new Image();
    img.onload = () => {
      ctx.drawImage(img, 0, 0);

      // Crop rectangle (blue dashed)
      if (crop) {
        ctx.strokeStyle = '#1890ff';
        ctx.lineWidth = 2;
        ctx.setLineDash([6, 4]);
        ctx.strokeRect(crop.x + 0.5, crop.y + 0.5, crop.w, crop.h);
        ctx.fillStyle = 'rgba(24, 144, 255, 0.12)';
        ctx.fillRect(crop.x, crop.y, crop.w, crop.h);
      }

      // Detection result overlays
      if (result?.kind === 'template' && result.match) {
        const m = result.match;
        drawLabeledRect(ctx, m.x, m.y, m.w, m.h, '#52c41a',
          `${result.templateName} · ${m.confidence.toFixed(3)}`);
      } else if (result?.kind === 'colors') {
        for (const b of result.blobs) {
          drawLabeledRect(ctx, b.x, b.y, b.w, b.h, '#fa8c16', `area=${b.area}`);
        }
      } else if (result?.kind === 'percentBar') {
        // Bar bbox (template match) in purple — outline only so the strip stands out.
        if (result.bar) {
          ctx.strokeStyle = '#722ed1';
          ctx.lineWidth = 2;
          ctx.setLineDash([6, 3]);
          ctx.strokeRect(result.bar.x + 0.5, result.bar.y + 0.5, result.bar.w, result.bar.h);
        }
        // Strip (auto-discovered fill row) in cyan with the fill % label.
        if (result.strip) {
          drawLabeledRect(ctx, result.strip.x, result.strip.y, result.strip.w, result.strip.h,
            '#13c2c2', `fill=${(result.fill * 100).toFixed(1)}%`);
        }
      }
    };
    img.src = `data:image/png;base64,${capture.pngBase64}`;
  }, [capture, crop, result]);

  useEffect(() => { drawOverlay(); }, [drawOverlay]);

  // ---------------- Mouse handlers ----------------

  const canvasToPixel = (e: React.MouseEvent<HTMLCanvasElement>): { x: number; y: number } | null => {
    const canvas = canvasRef.current;
    if (!canvas || !capture) return null;
    const rect = canvas.getBoundingClientRect();
    const scaleX = capture.width / rect.width;
    const scaleY = capture.height / rect.height;
    return {
      x: Math.max(0, Math.min(capture.width - 1, Math.round((e.clientX - rect.left) * scaleX))),
      y: Math.max(0, Math.min(capture.height - 1, Math.round((e.clientY - rect.top) * scaleY))),
    };
  };

  const sampleColor = (x: number, y: number): { r: number; g: number; b: number } | null => {
    const canvas = canvasRef.current;
    if (!canvas) return null;
    const ctx = canvas.getContext('2d');
    if (!ctx) return null;
    const data = ctx.getImageData(x, y, 1, 1).data;
    return { r: data[0], g: data[1], b: data[2] };
  };

  const onMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = canvasToPixel(e);
    if (!p) return;
    const c = sampleColor(p.x, p.y);
    setHover({ x: p.x, y: p.y, r: c?.r ?? 0, g: c?.g ?? 0, b: c?.b ?? 0 });
    if (dragStartRef.current) {
      const start = dragStartRef.current;
      setCrop({
        x: Math.min(start.x, p.x),
        y: Math.min(start.y, p.y),
        w: Math.abs(p.x - start.x),
        h: Math.abs(p.y - start.y),
      });
    }
  };

  const onMouseDown = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = canvasToPixel(e);
    if (!p) return;
    if (e.altKey) {
      // Alt+click → pick color into the active method's config
      const c = sampleColor(p.x, p.y);
      if (!c) return;
      if (method === 'colors') setColorsConfig((cfg) => ({ ...cfg, color: c }));
      else if (method === 'percentBar') setBarConfig((cfg) => ({ ...cfg, color: c }));
      message.success(t('capture.colorPicked', 'Picked color rgb({{r}}, {{g}}, {{b}})', c));
      return;
    }
    dragStartRef.current = p;
    setCrop({ x: p.x, y: p.y, w: 0, h: 0 });
  };

  const onMouseUp = () => { dragStartRef.current = null; };
  const onMouseLeave = () => { dragStartRef.current = null; setHover(undefined); };

  // ---------------- Crop save ----------------

  const cropPngBase64 = useMemo(() => {
    if (!capture || !crop || crop.w < 2 || crop.h < 2) return undefined;
    const off = document.createElement('canvas');
    off.width = crop.w;
    off.height = crop.h;
    const ctx = off.getContext('2d');
    if (!ctx) return undefined;
    const src = canvasRef.current;
    if (!src) return undefined;
    ctx.drawImage(src, crop.x, crop.y, crop.w, crop.h, 0, 0, crop.w, crop.h);
    return off.toDataURL('image/png').replace(/^data:image\/png;base64,/, '');
  }, [capture, crop]);

  const onSave = async (values: { name: string; description?: string }) => {
    if (!profileId) { message.error(t('capture.noProfile', 'No active profile.')); return; }
    if (!cropPngBase64) { message.error(t('capture.noCrop', 'Drag a rectangle on the image first.')); return; }
    try {
      const saved = await templateService.save(profileId, {
        name: values.name,
        description: values.description,
        pngBase64: cropPngBase64,
      });
      const inserted = useEditorBridgeStore.getState().insertAtCursor(`vision.find('${saved.name}')`);
      message.success(inserted
        ? t('capture.savedAndInserted', 'Template saved and inserted at cursor')
        : t('capture.saved', 'Template saved'));
      setSaveModalOpen(false);
      saveForm.resetFields();
      await refreshTemplates();
    } catch (err) { message.error(String(err)); }
  };

  const onDeleteTemplate = async (id: string) => {
    if (!profileId) return;
    await templateService.delete(profileId, id);
    await refreshTemplates();
  };

  if (!profileId) {
    return (
      <div className="capture-panel">
        <CompactAlert type="info" message={t('capture.selectProfile', 'Select a profile to capture and save templates.')} />
      </div>
    );
  }

  // ============ render ============

  return (
    <div className="capture-panel">
      {/* Row 1 — capture bar: window picker + Capture button. */}
      <div className="capture-panel__bar">
        <CompactSpace wrap>
          <CompactSelect
            showSearch
            placeholder={t('runner.pickWindow', 'Pick a window')}
            value={windowHandle}
            onChange={setWindowHandle}
            filterOption={(input, opt) => String(opt?.label ?? '').toLowerCase().includes(input.toLowerCase())}
            options={windows.map((w) => ({
              value: w.handle,
              label: `${w.title} — ${w.processName} [${w.className}] ${w.width}×${w.height}`,
            }))}
            style={{ minWidth: 320 }}
          />
          <Tooltip title={t('runner.refreshWindows', 'Refresh windows')}>
            <CompactButton icon={<ReloadOutlined />} onClick={refreshWindows} />
          </Tooltip>
          <CompactPrimaryButton icon={<CameraOutlined />} loading={grabbing} onClick={grab}>
            {t('capture.grab', 'Capture')}
          </CompactPrimaryButton>
          {capture && <Tag>{capture.width} × {capture.height}</Tag>}
        </CompactSpace>
      </div>

      {/* Row 2 — toolbar: crop save + snippet inserts. Always visible under capture bar. */}
      <div className="capture-panel__bar capture-panel__bar--secondary">
        <CompactSpace wrap size={4}>
          <CompactPrimaryButton
            size="small"
            icon={<ScissorOutlined />}
            disabled={!crop || crop.w < 2 || crop.h < 2}
            onClick={() => setSaveModalOpen(true)}
          >
            {t('capture.saveCrop', 'Save as template')}
          </CompactPrimaryButton>
          <Tooltip title={t('capture.insertPoint.tip', 'Insert {x, y} for the hovered pixel')}>
            <CompactButton size="small" icon={<PushpinOutlined />} disabled={!hover}
              onClick={() => hover && insertSnippet(`{ x: ${hover.x}, y: ${hover.y} }`)}>
              {t('capture.insertPoint', 'Point')}
            </CompactButton>
          </Tooltip>
          <Tooltip title={t('capture.insertColor.tip', 'Insert vision.colorAt(x, y) for the hovered pixel')}>
            <CompactButton size="small" icon={<BgColorsOutlined />} disabled={!hover}
              onClick={() => hover && insertSnippet(`vision.colorAt(${hover.x}, ${hover.y})`)}>
              {t('capture.insertColor', 'Color')}
            </CompactButton>
          </Tooltip>
          <Tooltip title={t('capture.insertRoi.tip', 'Insert {x, y, w, h} for the dragged rectangle')}>
            <CompactButton size="small" icon={<BorderOutlined />} disabled={!crop || crop.w < 2}
              onClick={() => crop && insertSnippet(`{ x: ${crop.x}, y: ${crop.y}, w: ${crop.w}, h: ${crop.h} }`)}>
              {t('capture.insertRoi', 'ROI')}
            </CompactButton>
          </Tooltip>
        </CompactSpace>
      </div>

      <div className="capture-panel__grid">
        <div className="capture-panel__canvas-wrap">
          {capture ? (
            <>
              <canvas
                ref={canvasRef}
                className="capture-panel__canvas"
                onMouseDown={onMouseDown}
                onMouseMove={onMouseMove}
                onMouseUp={onMouseUp}
                onMouseLeave={onMouseLeave}
              />
              <div className="capture-panel__hud">
                {hover && (
                  <div className="capture-panel__hud-row">
                    ({hover.x}, {hover.y}) · rgb({hover.r}, {hover.g}, {hover.b})
                  </div>
                )}
                {crop && crop.w > 0 && crop.h > 0 && (
                  <div className="capture-panel__hud-row capture-panel__hud-row--crop">
                    crop ({crop.x}, {crop.y}) {crop.w}×{crop.h}
                  </div>
                )}
                <div className="capture-panel__hud-row capture-panel__hud-row--hint">
                  {t('capture.canvasHint', 'drag = crop · alt+click = pick color')}
                </div>
              </div>
            </>
          ) : (
            <div className="capture-panel__empty">
              <CameraOutlined className="capture-panel__empty-icon" />
              <div className="capture-panel__empty-title">
                {t('capture.empty.title', 'No frame captured yet')}
              </div>
              <div className="capture-panel__empty-hint">
                {t('capture.empty.hint', 'Pick the target window above, then press Capture.')}
              </div>
            </div>
          )}
        </div>

        <div className="capture-panel__sidebar">
          <CompactCard extraCompact denseHeader title={t('capture.templates.title', 'Templates')}>
            {templates.length === 0 ? (
              <div className="capture-panel__empty-text">{t('capture.templates.empty', 'No templates yet.')}</div>
            ) : (
              <List
                size="small"
                dataSource={templates}
                renderItem={(tpl) => (
                  <List.Item
                    actions={[
                      <Tooltip key="test" title={t('capture.detect.testThis', 'Test this template')}>
                        <CompactButton size="small" type="text" icon={<AimOutlined />}
                          disabled={!capture}
                          onClick={() => { setMethod('template'); setTplConfig((c) => ({ ...c, name: tpl.name })); }} />
                      </Tooltip>,
                      <Popconfirm key="del"
                        title={t('capture.templates.deleteConfirm', 'Delete this template?')}
                        okText={t('common.delete')} cancelText={t('common.cancel')}
                        okButtonProps={{ danger: true }}
                        onConfirm={() => void onDeleteTemplate(tpl.id)}>
                        <CompactDangerButton size="small" type="text" icon={<DeleteOutlined />} />
                      </Popconfirm>,
                    ]}
                  >
                    <div className="capture-panel__template-name">
                      {tpl.name}
                      {tpl.description && <div style={{ fontSize: 12, color: 'var(--color-text-tertiary)' }}>{tpl.description}</div>}
                    </div>
                  </List.Item>
                )}
              />
            )}
          </CompactCard>

          {/* === Detection === */}
          <CompactCard
            extraCompact
            denseHeader
            title={<span><ThunderboltOutlined /> {t('capture.detect.title', 'Detect')}</span>}
          >
            <CompactSegmented
              value={method}
              onChange={(v) => { setMethod(v as DetectionMethod); setResult(undefined); }}
              // Drop icons — at sidebar width the icon + label combo can clip the
              // longest option ("Template"). Text-only is unambiguous and always fits.
              options={[
                { value: 'template', label: t('capture.detect.method.template', 'Template') },
                { value: 'colors', label: t('capture.detect.method.colors', 'Colors') },
                { value: 'percentBar', label: t('capture.detect.method.bar', 'Bar') },
              ]}
            />

            {method === 'template' && (
              <TemplateConfigForm
                config={tplConfig}
                templates={templates}
                onChange={setTplConfig}
              />
            )}
            {method === 'colors' && (
              <ColorsConfigForm config={colorsConfig} onChange={setColorsConfig} />
            )}
            {method === 'percentBar' && (
              <PercentBarConfigForm
                config={barConfig}
                templates={templates}
                onChange={setBarConfig}
              />
            )}

            <div className="capture-panel__detect-actions">
              <CompactPrimaryButton
                block
                icon={<PlayCircleOutlined />}
                loading={detecting}
                disabled={!capture}
                onClick={runActiveMethod}
              >
                {t('capture.detect.run', 'Run detection')}
              </CompactPrimaryButton>
              {result && (
                <CompactButton
                  block
                  size="small"
                  style={{ marginTop: 6 }}
                  onClick={insertActiveResultSnippet}
                >
                  {t('capture.detect.insert', 'Insert snippet')}
                </CompactButton>
              )}
            </div>

            {result && <DetectionResultDisplay result={result} />}
          </CompactCard>
        </div>
      </div>

      <FormDialog
        visible={saveModalOpen}
        title={t('capture.saveCrop', 'Save as template')}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        onCancel={() => setSaveModalOpen(false)}
        onOk={() => saveForm.submit()}
      >
        <Form form={saveForm} layout="vertical" onFinish={onSave}>
          <Form.Item
            label={t('capture.templateName', 'Template name')}
            name="name"
            rules={[{ required: true, message: t('script.create.nameRequired', 'Name is required') }]}
          >
            <CompactInput placeholder="bobber" />
          </Form.Item>
          <Form.Item
            label={t('capture.templateDescription', 'Description (optional)')}
            name="description"
          >
            <CompactInput placeholder={t('capture.templateDescription.placeholder', 'What this captures, when to use it...') as string} />
          </Form.Item>
        </Form>
      </FormDialog>
    </div>
  );
};

// ============ Per-method config sub-components ============

const TemplateConfigForm: React.FC<{
  config: TemplateConfig;
  templates: TemplateInfo[];
  onChange: (c: TemplateConfig) => void;
}> = ({ config, templates, onChange }) => {
  const { t } = useTranslation();
  return (
    <>
      <div className="capture-panel__detect-row">
        <CompactSelect
          placeholder={t('capture.detect.pickTemplate', 'Pick a template')}
          value={config.name}
          onChange={(name) => onChange({ ...config, name })}
          options={templates.map((tpl) => ({ value: tpl.name, label: tpl.name }))}
          style={{ width: '100%' }}
          disabled={templates.length === 0}
        />
      </div>
      <div className="capture-panel__detect-row">
        <span className="capture-panel__detect-label">
          {t('capture.detect.confidence', 'Min confidence')}: {config.minConfidence.toFixed(2)}
        </span>
        <Slider min={0.5} max={0.99} step={0.01}
          value={config.minConfidence}
          onChange={(v) => onChange({ ...config, minConfidence: v })} />
      </div>
      <div className="capture-panel__detect-row">
        <span className="capture-panel__detect-label">
          {t('capture.detect.scale', 'Scale')}: {config.scale.toFixed(2)} ({describeScale(config.scale)})
        </span>
        <Slider min={0.25} max={1.0} step={0.05}
          value={config.scale}
          onChange={(v) => onChange({ ...config, scale: v })} />
      </div>
      <div className="capture-panel__detect-row">
        <label className="capture-panel__inline-label">
          <input type="checkbox" checked={config.grayscale}
            onChange={(e) => onChange({ ...config, grayscale: e.target.checked })} />
          {' '}{t('capture.detect.grayscale', 'Grayscale (~3× faster)')}
        </label>
      </div>
    </>
  );
};

const ColorsConfigForm: React.FC<{ config: ColorsConfig; onChange: (c: ColorsConfig) => void }> = ({ config, onChange }) => {
  const { t } = useTranslation();
  const c = config.color;
  return (
    <>
      <ColorRow
        label={t('capture.detect.color', 'Target color')}
        color={c}
        onChange={(color) => onChange({ ...config, color })}
      />
      <div className="capture-panel__detect-row">
        <span className="capture-panel__detect-label">
          {t('capture.detect.tolerance', 'Tolerance')}: ±{config.tolerance}
        </span>
        <Slider min={0} max={120} step={5}
          value={config.tolerance}
          onChange={(v) => onChange({ ...config, tolerance: v })} />
      </div>
      <div className="capture-panel__detect-row">
        <span className="capture-panel__detect-label">
          {t('capture.detect.minArea', 'Min blob area (px²)')}: {config.minArea}
        </span>
        <Slider min={1} max={5000} step={10}
          value={config.minArea}
          onChange={(v) => onChange({ ...config, minArea: v })} />
      </div>
    </>
  );
};

const PercentBarConfigForm: React.FC<{
  config: PercentBarConfig;
  templates: TemplateInfo[];
  onChange: (c: PercentBarConfig) => void;
}> = ({ config, templates, onChange }) => {
  const { t } = useTranslation();
  const c = config.color;
  return (
    <>
      <div className="capture-panel__detect-row">
        <span className="capture-panel__detect-label">
          {t('capture.detect.bar.template', 'Bar template')}
        </span>
        <CompactSelect
          placeholder={t('capture.detect.pickTemplate', 'Pick a template')}
          value={config.templateName}
          onChange={(name) => onChange({ ...config, templateName: name })}
          options={templates.map((tpl) => ({ value: tpl.name, label: tpl.name }))}
          style={{ width: '100%' }}
          disabled={templates.length === 0}
        />
      </div>
      <ColorRow
        label={t('capture.detect.bar.fillColor', 'Fill color')}
        color={c}
        onChange={(color) => onChange({ ...config, color })}
      />
      <div className="capture-panel__detect-row">
        <span className="capture-panel__detect-label">
          {t('capture.detect.tolerance', 'Tolerance')}: ±{config.tolerance}
        </span>
        <Slider min={0} max={120} step={5}
          value={config.tolerance}
          onChange={(v) => onChange({ ...config, tolerance: v })} />
      </div>
      <div className="capture-panel__detect-row">
        <span className="capture-panel__detect-label">
          {t('capture.detect.confidence', 'Min confidence')}: {config.minConfidence.toFixed(2)}
        </span>
        <Slider min={0.5} max={0.99} step={0.01}
          value={config.minConfidence}
          onChange={(v) => onChange({ ...config, minConfidence: v })} />
      </div>
    </>
  );
};

/**
 * Color picker row — single antd ColorPicker (palette + slider + RGB/HEX inputs in a
 * popover) plus the alt+click hint. Replaces the old 3-input RGB row that overflowed
 * the sidebar at narrow widths.
 */
const ColorRow: React.FC<{
  label: string;
  color: { r: number; g: number; b: number };
  onChange: (c: { r: number; g: number; b: number }) => void;
}> = ({ label, color, onChange }) => {
  const { t } = useTranslation();
  return (
    <div className="capture-panel__detect-row capture-panel__detect-row--color">
      <span className="capture-panel__detect-label">{label}</span>
      <div className="capture-panel__color-row">
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
        <span className="capture-panel__color-hint">
          {t('capture.detect.color.altClickHint', 'alt+click canvas to pick')}
        </span>
      </div>
    </div>
  );
};

const DetectionResultDisplay: React.FC<{ result: DetectionResult }> = ({ result }) => {
  const { t } = useTranslation();
  return (
    <div className="capture-panel__detect-row">
      {result.kind === 'template' && (
        result.match ? (
          <Tag color="green">
            {t('capture.detect.matched', 'Match @ ({{x}}, {{y}}) — {{conf}} — {{ms}}ms', {
              x: result.match.cx, y: result.match.cy,
              conf: result.match.confidence.toFixed(3), ms: result.durationMs,
            })}
          </Tag>
        ) : (
          <Tag>{t('capture.detect.noMatch', 'No match — {{ms}}ms', { ms: result.durationMs })}</Tag>
        )
      )}
      {result.kind === 'colors' && (
        <Tag color={result.blobs.length > 0 ? 'orange' : 'default'}>
          {t('capture.detect.colorsResult', '{{n}} blobs — {{ms}}ms', { n: result.blobs.length, ms: result.durationMs })}
        </Tag>
      )}
      {result.kind === 'percentBar' && (
        result.bar ? (
          <Tag color="cyan">
            {t('capture.detect.barResult', 'Fill {{pct}} — {{ms}}ms', {
              pct: (result.fill * 100).toFixed(1) + '%', ms: result.durationMs,
            })}
          </Tag>
        ) : (
          <Tag>{t('capture.detect.bar.notFound', 'Template not found — {{ms}}ms', { ms: result.durationMs })}</Tag>
        )
      )}
    </div>
  );
};

// ============ helpers ============

function describeScale(scale: number): string {
  if (scale >= 0.99) return 'full res';
  if (scale >= 0.5) return `${(1 / (scale * scale)).toFixed(1)}× faster`;
  return `${Math.round(1 / (scale * scale))}× faster`;
}

function drawLabeledRect(
  ctx: CanvasRenderingContext2D,
  x: number, y: number, w: number, h: number,
  color: string,
  label: string,
) {
  ctx.strokeStyle = color;
  ctx.lineWidth = 3;
  ctx.setLineDash([]);
  ctx.strokeRect(x + 0.5, y + 0.5, w, h);
  ctx.fillStyle = withAlpha(color, 0.15);
  ctx.fillRect(x, y, w, h);
  ctx.font = '14px sans-serif';
  const textWidth = ctx.measureText(label).width;
  ctx.fillStyle = withAlpha(color, 0.9);
  ctx.fillRect(x, Math.max(0, y - 18), textWidth + 8, 18);
  ctx.fillStyle = '#ffffff';
  ctx.fillText(label, x + 4, Math.max(12, y - 5));
}

function withAlpha(hex: string, alpha: number): string {
  // Quick #rrggbb → rgba(.., alpha) — the colors we use are all hex literals.
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
