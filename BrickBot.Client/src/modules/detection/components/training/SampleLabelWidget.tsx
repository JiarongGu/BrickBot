import React from 'react';
import { Slider } from 'antd';
import { useTranslation } from 'react-i18next';
import { CompactButton, CompactSpace } from '@/shared/components/compact';
import type { DetectionKind } from '../../types';

/**
 * Kind-aware label editor used during training. Only `pattern` and `bar` need labels:
 *   - pattern → ✓ Positive / ✗ Negative (sample contains the element or doesn't).
 *   - bar     → 0..1 fill ratio slider + 0/25/50/75/100 quick buttons.
 *
 * Tracker + text are one-shot kinds (single init frame, no labels). When the parent
 * passes those kinds, the widget renders nothing.
 */
export const SampleLabelWidget: React.FC<{
  kind: DetectionKind;
  value: string;
  onChange: (v: string) => void;
}> = ({ kind, value, onChange }) => {
  const { t } = useTranslation();

  if (kind === 'bar') {
    const num = Number.isFinite(parseFloat(value)) ? parseFloat(value) : 0;
    return (
      <div className="samples-review__label">
        <span className="samples-review__label-key">{t('detection.train.label', 'Label')}: <b>{value || '—'}</b></span>
        <div className="samples-review__quickbuttons">
          {[0, 0.25, 0.5, 0.75, 1].map((v) => (
            <CompactButton
              key={v}
              size="small"
              type={Math.abs(num - v) < 0.01 ? 'primary' : 'text'}
              onClick={() => onChange(v.toFixed(2))}
            >
              {(v * 100).toFixed(0)}%
            </CompactButton>
          ))}
        </div>
        <Slider
          min={0}
          max={1}
          step={0.01}
          value={num}
          onChange={(v) => onChange((v as number).toFixed(2))}
        />
      </div>
    );
  }

  if (kind === 'pattern') {
    const isTrue = ['true', 'yes', '1', '+', 'positive'].includes(value.trim().toLowerCase());
    const isFalse = !isTrue && value.trim().length > 0;
    return (
      <div className="samples-review__label">
        <span className="samples-review__label-key">{t('detection.train.label', 'Label')}:</span>
        <CompactSpace size={4}>
          <CompactButton size="small" type={isTrue ? 'primary' : 'text'} onClick={() => onChange('true')}>
            ✓ {t('detection.train.positive', 'Positive (visible)')}
          </CompactButton>
          <CompactButton size="small" type={isFalse ? 'primary' : 'text'} onClick={() => onChange('false')}>
            ✗ {t('detection.train.negative', 'Negative (absent)')}
          </CompactButton>
        </CompactSpace>
      </div>
    );
  }

  // Tracker / text don't use per-sample labels in this widget.
  return null;
};
