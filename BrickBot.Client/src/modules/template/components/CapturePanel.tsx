import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Empty, Form, List, Popconfirm, Tag, Tooltip, message } from 'antd';
import {
  CameraOutlined,
  DeleteOutlined,
  ReloadOutlined,
  ScissorOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  CompactAlert,
  CompactButton,
  CompactCard,
  CompactDangerButton,
  CompactInput,
  CompactPrimaryButton,
  CompactSelect,
  CompactSpace,
} from '@/shared/components/compact';
import { FormDialog } from '@/shared/components/dialogs';
import { useProfileStore } from '@/modules/profile';
import { captureService } from '@/modules/runner/services/captureService';
import type { WindowInfo } from '@/modules/runner/types';
import { templateService } from '../services/templateService';
import type { CropRect } from '../types';
import './CapturePanel.css';

interface CaptureState {
  pngBase64: string;
  width: number;
  height: number;
}

/**
 * Capture & Templates panel — pick a target window, grab one frame, hover for pixel
 * coordinates + BGR color, drag a rectangle to crop a region, save it as a PNG into
 * the active profile's templates dir for `vision.find('name.png')` to load.
 *
 * Used in two places: a Drawer on the Scripts editor toolbar (mid-edit), and the
 * Tools tab "Captures" sub-tab (standalone).
 */
export const CapturePanel: React.FC = () => {
  const { t } = useTranslation();
  const profileId = useProfileStore((s) => s.activeProfileId);

  const [windows, setWindows] = useState<WindowInfo[]>([]);
  const [windowHandle, setWindowHandle] = useState<number | undefined>();
  const [capture, setCapture] = useState<CaptureState | undefined>();
  const [grabbing, setGrabbing] = useState(false);
  const [crop, setCrop] = useState<CropRect | undefined>();
  const [hover, setHover] = useState<{ x: number; y: number; r: number; g: number; b: number } | undefined>();
  const [templates, setTemplates] = useState<string[]>([]);
  const [saveModalOpen, setSaveModalOpen] = useState(false);
  const [saveForm] = Form.useForm();

  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const dragStartRef = useRef<{ x: number; y: number } | null>(null);

  const refreshWindows = useCallback(async () => {
    const list = await captureService.listWindows();
    setWindows(list);
    if (list.length > 0 && !windowHandle) setWindowHandle(list[0].handle);
  }, [windowHandle]);

  const refreshTemplates = useCallback(async () => {
    if (!profileId) return;
    const { templates: names } = await templateService.list(profileId);
    setTemplates(names);
  }, [profileId]);

  useEffect(() => { void refreshWindows(); }, [refreshWindows]);
  useEffect(() => { void refreshTemplates(); }, [refreshTemplates]);

  const grab = useCallback(async () => {
    if (!windowHandle) {
      message.warning(t('capture.pickWindowFirst', 'Pick a window first.'));
      return;
    }
    setGrabbing(true);
    setCrop(undefined);
    try {
      const result = await captureService.grabPng(windowHandle);
      setCapture(result);
    } catch (err) {
      message.error(String(err));
    } finally {
      setGrabbing(false);
    }
  }, [windowHandle, t]);

  // Render the captured PNG into the canvas whenever it changes.
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

  // Draw the crop rectangle overlay on top of the image.
  const drawOverlay = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas || !capture) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    // Re-blit the source image first to wipe any previous overlay.
    const img = new Image();
    img.onload = () => {
      ctx.drawImage(img, 0, 0);
      if (crop) {
        ctx.strokeStyle = '#1890ff';
        ctx.lineWidth = 2;
        ctx.setLineDash([6, 4]);
        ctx.strokeRect(crop.x + 0.5, crop.y + 0.5, crop.w, crop.h);
        ctx.fillStyle = 'rgba(24, 144, 255, 0.12)';
        ctx.fillRect(crop.x, crop.y, crop.w, crop.h);
      }
    };
    img.src = `data:image/png;base64,${capture.pngBase64}`;
  }, [capture, crop]);

  useEffect(() => { drawOverlay(); }, [drawOverlay]);

  const canvasToPixel = (e: React.MouseEvent<HTMLCanvasElement>): { x: number; y: number } | null => {
    const canvas = canvasRef.current;
    if (!canvas || !capture) return null;
    const rect = canvas.getBoundingClientRect();
    // Image rendered at native resolution scaled into the canvas client rect; map back.
    const scaleX = capture.width / rect.width;
    const scaleY = capture.height / rect.height;
    return {
      x: Math.max(0, Math.min(capture.width - 1, Math.round((e.clientX - rect.left) * scaleX))),
      y: Math.max(0, Math.min(capture.height - 1, Math.round((e.clientY - rect.top) * scaleY))),
    };
  };

  const onMouseMove = (e: React.MouseEvent<HTMLCanvasElement>) => {
    const p = canvasToPixel(e);
    if (!p || !canvasRef.current) return;

    // Read pixel color from the source image (before overlay), via a temp canvas.
    const ctx = canvasRef.current.getContext('2d');
    if (ctx) {
      const data = ctx.getImageData(p.x, p.y, 1, 1).data;
      setHover({ x: p.x, y: p.y, r: data[0], g: data[1], b: data[2] });
    } else {
      setHover({ x: p.x, y: p.y, r: 0, g: 0, b: 0 });
    }

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
    dragStartRef.current = p;
    setCrop({ x: p.x, y: p.y, w: 0, h: 0 });
  };

  const onMouseUp = () => {
    dragStartRef.current = null;
  };

  const onMouseLeave = () => {
    dragStartRef.current = null;
    setHover(undefined);
  };

  const cropPngBase64 = useMemo(() => {
    if (!capture || !crop || crop.w < 2 || crop.h < 2) return undefined;
    const off = document.createElement('canvas');
    off.width = crop.w;
    off.height = crop.h;
    const ctx = off.getContext('2d');
    if (!ctx) return undefined;
    const img = new Image();
    // synchronous drawImage from data URL won't work without onload; precompute in handler instead.
    // Workaround: use the on-screen canvas as the source.
    const src = canvasRef.current;
    if (!src) return undefined;
    ctx.drawImage(src, crop.x, crop.y, crop.w, crop.h, 0, 0, crop.w, crop.h);
    const dataUrl = off.toDataURL('image/png');
    return dataUrl.replace(/^data:image\/png;base64,/, '');
  }, [capture, crop]);

  const onSave = async (values: { name: string }) => {
    if (!profileId) {
      message.error(t('capture.noProfile', 'No active profile.'));
      return;
    }
    if (!cropPngBase64) {
      message.error(t('capture.noCrop', 'Drag a rectangle on the image first.'));
      return;
    }
    try {
      await templateService.save(profileId, values.name, cropPngBase64);
      message.success(t('capture.saved', 'Template saved'));
      setSaveModalOpen(false);
      saveForm.resetFields();
      await refreshTemplates();
    } catch (err) {
      message.error(String(err));
    }
  };

  const onDeleteTemplate = async (name: string) => {
    if (!profileId) return;
    await templateService.delete(profileId, name);
    await refreshTemplates();
  };

  if (!profileId) {
    return (
      <div className="capture-panel">
        <CompactAlert type="info" message={t('capture.selectProfile', 'Select a profile to capture and save templates.')} />
      </div>
    );
  }

  return (
    <div className="capture-panel">
      <div className="capture-panel__controls">
        <CompactSpace wrap>
          <CompactSelect
            showSearch
            placeholder={t('runner.pickWindow', 'Pick a window')}
            value={windowHandle}
            onChange={setWindowHandle}
            filterOption={(input, opt) => String(opt?.label ?? '').toLowerCase().includes(input.toLowerCase())}
            options={windows.map((w) => ({
              value: w.handle,
              label: `${w.title} (${w.processName}) ${w.width}x${w.height}`,
            }))}
            style={{ minWidth: 320 }}
          />
          <Tooltip title={t('runner.refreshWindows', 'Refresh windows')}>
            <CompactButton icon={<ReloadOutlined />} onClick={refreshWindows} />
          </Tooltip>
          <CompactPrimaryButton icon={<CameraOutlined />} loading={grabbing} onClick={grab}>
            {t('capture.grab', 'Capture')}
          </CompactPrimaryButton>
          <CompactButton
            icon={<ScissorOutlined />}
            disabled={!crop || crop.w < 2 || crop.h < 2}
            onClick={() => setSaveModalOpen(true)}
          >
            {t('capture.saveCrop', 'Save crop as template')}
          </CompactButton>
          {capture && (
            <Tag>{capture.width} x {capture.height}</Tag>
          )}
          {hover && (
            <Tag>
              ({hover.x}, {hover.y}) — rgb({hover.r},{hover.g},{hover.b})
            </Tag>
          )}
          {crop && crop.w > 0 && crop.h > 0 && (
            <Tag color="blue">crop: ({crop.x}, {crop.y}) {crop.w}×{crop.h}</Tag>
          )}
        </CompactSpace>
      </div>

      <div className="capture-panel__grid">
        <div className="capture-panel__canvas-wrap">
          {capture ? (
            <canvas
              ref={canvasRef}
              className="capture-panel__canvas"
              onMouseDown={onMouseDown}
              onMouseMove={onMouseMove}
              onMouseUp={onMouseUp}
              onMouseLeave={onMouseLeave}
            />
          ) : (
            <Empty
              image={<CameraOutlined style={{ fontSize: 56, color: 'var(--color-text-tertiary)' }} />}
              description={t('capture.empty', 'Pick a window and press Capture.')}
            />
          )}
        </div>

        <CompactCard extraCompact title={t('capture.templates.title', 'Templates')} className="capture-panel__sidebar">
          {templates.length === 0 ? (
            <div className="capture-panel__empty-text">{t('capture.templates.empty', 'No templates yet.')}</div>
          ) : (
            <List
              size="small"
              dataSource={templates}
              renderItem={(name) => (
                <List.Item
                  actions={[
                    <Popconfirm
                      key="del"
                      title={t('capture.templates.deleteConfirm', 'Delete this template?')}
                      okText={t('common.delete')}
                      cancelText={t('common.cancel')}
                      okButtonProps={{ danger: true }}
                      onConfirm={() => void onDeleteTemplate(name)}
                    >
                      <CompactDangerButton size="small" type="text" icon={<DeleteOutlined />} />
                    </Popconfirm>,
                  ]}
                >
                  <span className="capture-panel__template-name">{name}.png</span>
                </List.Item>
              )}
            />
          )}
        </CompactCard>
      </div>

      <FormDialog
        visible={saveModalOpen}
        title={t('capture.saveCrop', 'Save crop as template')}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        onCancel={() => setSaveModalOpen(false)}
        onOk={() => saveForm.submit()}
      >
        <Form form={saveForm} layout="vertical" onFinish={onSave}>
          <Form.Item
            label={t('capture.templateName', 'Template name')}
            name="name"
            rules={[
              { required: true, message: t('script.create.nameRequired', 'Name is required') },
              { pattern: /^[A-Za-z0-9_\-]+$/, message: t('script.create.nameInvalid', 'Letters, numbers, _, - only') },
            ]}
          >
            <CompactInput placeholder="bobber" addonAfter=".png" />
          </Form.Item>
        </Form>
      </FormDialog>
    </div>
  );
};
