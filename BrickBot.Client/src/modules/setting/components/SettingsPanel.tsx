import React, { useEffect } from 'react';
import { Col, Form, Popconfirm, Row, Tabs } from 'antd';
import { ReloadOutlined, SettingOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  CompactCard,
  CompactDangerButton,
  CompactSelect,
  CompactWarningButton,
} from '@/shared/components/compact';
import { useSettingsStore } from '../store/settingsStore';
import {
  resetAll,
  resetWindowState,
  setAnnotationLevel,
  setLanguage,
  setLogLevel,
  setTheme,
} from '../operations/settingsOperations';
import type { AnnotationLevel, LogLevel, ThemeMode } from '../types';
import './SettingsPanel.css';

const LOG_LEVELS: LogLevel[] = ['All', 'Verbose', 'Debug', 'Info', 'Warn', 'Error', 'Off'];
const ANNOTATION_LEVELS: AnnotationLevel[] = ['all', 'more', 'less', 'off'];

/**
 * Settings screen. Uses the shared compact
 * library throughout (CompactCard / CompactSelect / CompactWarningButton / CompactDangerButton).
 */
export const SettingsPanel: React.FC = () => {
  const { t } = useTranslation();

  return (
    <div className="settings-view-container">
      <div className="settings-view-content-wrapper">
        <Tabs
          defaultActiveKey="global"
          items={[
            {
              key: 'global',
              label: (
                <span>
                  <SettingOutlined /> {t('settings.tabs.global')}
                </span>
              ),
              children: <GlobalSettingsTab />,
            },
          ]}
        />
      </div>
    </div>
  );
};

const GlobalSettingsTab: React.FC = () => {
  const { t } = useTranslation();
  const [form] = Form.useForm();

  const settings = useSettingsStore((s) => s.settings);
  const availableLanguages = useSettingsStore((s) => s.availableLanguages);
  const loading = useSettingsStore((s) => s.loading);

  useEffect(() => {
    if (settings) {
      form.setFieldsValue({
        theme: settings.theme,
        language: settings.language,
        logLevel: settings.logLevel,
        annotationLevel: settings.annotationLevel,
      });
    }
  }, [form, settings]);

  if (!settings) {
    return <CompactCard loading={loading} />;
  }

  return (
    <Form
      form={form}
      layout="vertical"
      initialValues={{
        theme: settings.theme,
        language: settings.language,
        logLevel: settings.logLevel,
        annotationLevel: settings.annotationLevel,
      }}
    >
      <CompactCard className="settings-view-card-margin">
        <Row gutter={16}>
          <Col span={12}>
            <Form.Item
              label={t('settings.theme.label')}
              name="theme"
              tooltip={t('settings.theme.tooltip', 'Light, dark, or follow the OS preference.')}
            >
              <CompactSelect<ThemeMode>
                onChange={(value) => void setTheme(value)}
                options={[
                  { value: 'light', label: t('settings.theme.light') },
                  { value: 'dark', label: t('settings.theme.dark') },
                  { value: 'auto', label: t('settings.theme.auto') },
                ]}
              />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item
              label={t('settings.language.label')}
              name="language"
              tooltip={t('settings.language.tooltip', 'UI display language.')}
            >
              <CompactSelect<string>
                onChange={(value) => void setLanguage(value)}
                options={availableLanguages.map((code) => ({ value: code, label: code }))}
              />
            </Form.Item>
          </Col>
        </Row>

        <Row gutter={16}>
          <Col span={12}>
            <Form.Item
              label={t('settings.logLevel.label')}
              name="logLevel"
              tooltip={t('settings.logLevel.tooltip', 'Backend file-log verbosity.')}
            >
              <CompactSelect<LogLevel>
                onChange={(value) => void setLogLevel(value)}
                options={LOG_LEVELS.map((level) => ({ value: level, label: level }))}
              />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item
              label={t('settings.annotationLevel.label')}
              name="annotationLevel"
              tooltip={t('settings.annotationLevel.tooltip', 'How much in-app help text to show.')}
            >
              <CompactSelect<AnnotationLevel>
                onChange={(value) => void setAnnotationLevel(value)}
                options={ANNOTATION_LEVELS.map((level) => ({
                  value: level,
                  label: t(`settings.annotationLevel.${level}`),
                }))}
              />
            </Form.Item>
          </Col>
        </Row>

        <Row gutter={16}>
          <Col span={12}>
            <Form.Item
              label={t('settings.window.reset')}
              tooltip={t('settings.window.resetTooltip')}
            >
              <CompactWarningButton icon={<ReloadOutlined />} onClick={() => void resetWindowState()} block>
                {t('settings.window.reset')}
              </CompactWarningButton>
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item label={t('settings.reset.label')}>
              <Popconfirm
                title={t('settings.reset.confirmTitle')}
                description={t('settings.reset.confirmDescription')}
                okText={t('common.confirm')}
                cancelText={t('common.cancel')}
                okButtonProps={{ danger: true }}
                onConfirm={() => void resetAll()}
              >
                <CompactDangerButton block>
                  {t('settings.reset.label')}
                </CompactDangerButton>
              </Popconfirm>
            </Form.Item>
          </Col>
        </Row>
      </CompactCard>
    </Form>
  );
};
