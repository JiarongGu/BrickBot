import React, { useEffect, useMemo, useState } from 'react';
import {
  Empty,
  Form,
  List,
  Popconfirm,
  Radio,
  Tag,
  Tooltip,
  message,
} from 'antd';
import {
  CameraOutlined,
  DeleteOutlined,
  FileOutlined,
  FolderOpenOutlined,
  PlusOutlined,
  SaveOutlined,
} from '@ant-design/icons';
import Editor from '@monaco-editor/react';
import { useTranslation } from 'react-i18next';
import classNames from 'classnames';
import {
  CompactAlert,
  CompactButton,
  CompactCard,
  CompactDangerButton,
  CompactInput,
  CompactPrimaryButton,
  CompactSpace,
} from '@/shared/components/compact';
import { SlideInScreen } from '@/shared/components/common';
import { FormDialog } from '@/shared/components/dialogs';
import { useProfileStore } from '@/modules/profile';
import { CapturePanel } from '@/modules/template';
import { useScriptStore } from '../store/scriptStore';
import { useEditorBridgeStore } from '../store/editorBridgeStore';
import {
  STARTER_TEMPLATES,
  createScript,
  deleteScript,
  loadScripts,
  saveSelected,
  selectScript,
} from '../operations/scriptOperations';
import type { ScriptKind } from '../types';
import './ScriptsView.css';

/**
 * Two-pane Scripts manager. File list (Main + Library) on the left, Monaco editor on the
 * right. Uses the shared compact component library + FormDialog for create. The Capture
 * Drawer surfaces the screenshot/template authoring tool while the user edits.
 */
export const ScriptsView: React.FC = () => {
  const { t } = useTranslation();
  const activeProfileId = useProfileStore((s) => s.activeProfileId);
  const files = useScriptStore((s) => s.files);
  const selected = useScriptStore((s) => s.selected);
  const loading = useScriptStore((s) => s.loading);

  const [createOpen, setCreateOpen] = useState(false);
  const [captureOpen, setCaptureOpen] = useState(false);

  useEffect(() => {
    if (activeProfileId) void loadScripts(activeProfileId);
  }, [activeProfileId]);

  useEffect(() => {
    // Drop the editor reference on unmount so the bridge doesn't dispatch into a
    // disposed Monaco instance (e.g. when the user navigates to a different tab).
    return () => useEditorBridgeStore.getState().setEditor(undefined);
  }, []);

  const grouped = useMemo(() => {
    const main = files.filter((f) => f.kind === 'main');
    const library = files.filter((f) => f.kind === 'library');
    return { main, library };
  }, [files]);

  if (!activeProfileId) {
    return (
      <div className="scripts-view-container">
        <CompactAlert
          type="info"
          message={t('script.noProfile', 'Select a profile from the header to manage its scripts.')}
        />
      </div>
    );
  }

  return (
    <div className="scripts-view-container">
      <div className="scripts-view-grid">
        <CompactCard
          extraCompact
          loading={loading}
          className="scripts-view-list"
          title={t('script.list.title', 'Scripts')}
          extra={
            <CompactButton size="small" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
              {t('common.create')}
            </CompactButton>
          }
        >
          <ScriptSection
            kind="main"
            label={t('script.section.main', 'Main')}
            description={t('script.section.mainHint', 'Top-level orchestrators — Runner picks one to execute.')}
            files={grouped.main}
            profileId={activeProfileId}
          />
          <ScriptSection
            kind="library"
            label={t('script.section.library', 'Library')}
            description={t('script.section.libraryHint', 'Preloaded before main runs. Define helpers / monitors / skills here.')}
            files={grouped.library}
            profileId={activeProfileId}
          />
        </CompactCard>

        <CompactCard
          extraCompact
          className="scripts-view-editor"
          title={
            selected ? (
              <CompactSpace>
                <Tag color={selected.kind === 'main' ? 'gold' : 'blue'}>{selected.kind}</Tag>
                <span>{selected.name}.ts</span>
                {selected.dirty && <Tag color="orange">{t('script.unsaved', 'Unsaved')}</Tag>}
                {selected.diagnostics.some((d) => d.severity === 'error') && (
                  <Tag color="red">
                    {t('script.errors', '{{count}} error', { count: selected.diagnostics.filter((d) => d.severity === 'error').length })}
                  </Tag>
                )}
              </CompactSpace>
            ) : (
              t('script.editor.empty', 'Select a script')
            )
          }
          extra={
            <CompactSpace>
              <Tooltip title={t('script.openCapture', 'Capture screen / save templates')}>
                <CompactButton size="small" icon={<CameraOutlined />} onClick={() => setCaptureOpen(true)}>
                  {t('script.capture', 'Capture')}
                </CompactButton>
              </Tooltip>
              {selected && (
                <Tooltip title={t('common.save')}>
                  <CompactPrimaryButton
                    size="small"
                    icon={<SaveOutlined />}
                    disabled={!selected.dirty}
                    onClick={async () => {
                      try {
                        await saveSelected(activeProfileId);
                        message.success(t('script.saved', 'Saved'));
                      } catch (err) {
                        message.error(String(err));
                      }
                    }}
                  >
                    {t('common.save')}
                  </CompactPrimaryButton>
                </Tooltip>
              )}
            </CompactSpace>
          }
          bodyStyle={{ padding: 0, height: 'calc(100vh - var(--layout-header-height) - var(--layout-statusbar-height) - 100px)' }}
        >
          {selected ? (
            <Editor
              height="100%"
              defaultLanguage="typescript"
              path={`file:///scripts/${selected.kind}/${selected.name}.ts`}
              value={selected.source}
              onChange={(v) => useScriptStore.getState().setSource(v ?? '')}
              onMount={(editor) => {
                // Register with the bridge so CapturePanel + future tools can paste
                // brickbot snippets at the cursor without depending on Monaco APIs.
                useEditorBridgeStore.getState().setEditor(editor);
              }}
              options={{ minimap: { enabled: false }, fontSize: 14, automaticLayout: true }}
            />
          ) : (
            <Empty
              image={<FileOutlined style={{ fontSize: 48, color: 'var(--color-text-tertiary)' }} />}
              description={t('script.editor.emptyHint', 'Pick a script on the left or create a new one.')}
              style={{ paddingTop: 80 }}
            />
          )}
        </CompactCard>
      </div>

      <CreateScriptDialog
        open={createOpen}
        profileId={activeProfileId}
        onClose={() => setCreateOpen(false)}
      />

      <SlideInScreen
        open={captureOpen}
        title={t('script.capture', 'Capture')}
        bodyClassName="scripts-view-capture-body"
        onClose={() => setCaptureOpen(false)}
      >
        <CapturePanel />
      </SlideInScreen>
    </div>
  );
};

const ScriptSection: React.FC<{
  kind: ScriptKind;
  label: string;
  description: string;
  files: { name: string; kind: ScriptKind }[];
  profileId: string;
}> = ({ kind, label, description, files, profileId }) => {
  const { t } = useTranslation();
  const selected = useScriptStore((s) => s.selected);

  return (
    <div className="scripts-view-section">
      <div className="scripts-view-section__header">
        <span className="scripts-view-section__label">
          <FolderOpenOutlined /> {label}
        </span>
        <span className="scripts-view-section__count">{files.length}</span>
      </div>
      <div className="scripts-view-section__hint">{description}</div>
      {files.length === 0 ? (
        <div className="scripts-view-section__empty">{t('script.empty', 'No scripts yet.')}</div>
      ) : (
        <List
          size="small"
          dataSource={files}
          renderItem={(file) => {
            const isActive = selected?.kind === file.kind && selected?.name === file.name;
            return (
              <List.Item
                className={classNames('scripts-view-item', { 'scripts-view-item--active': isActive })}
                onClick={() => void selectScript(profileId, file.kind, file.name)}
                actions={[
                  <Popconfirm
                    key="del"
                    title={t('script.delete.confirmTitle', 'Delete this script?')}
                    okText={t('common.delete')}
                    cancelText={t('common.cancel')}
                    okButtonProps={{ danger: true }}
                    onConfirm={(e) => {
                      e?.stopPropagation();
                      void deleteScript(profileId, file.kind, file.name);
                    }}
                  >
                    <CompactDangerButton
                      size="small"
                      type="text"
                      icon={<DeleteOutlined />}
                      onClick={(e) => e.stopPropagation()}
                    />
                  </Popconfirm>,
                ]}
              >
                <span className="scripts-view-item__name">{file.name}.ts</span>
              </List.Item>
            );
          }}
        />
      )}
    </div>
  );
};

const CreateScriptDialog: React.FC<{
  open: boolean;
  profileId: string;
  onClose: () => void;
}> = ({ open, profileId, onClose }) => {
  const { t } = useTranslation();
  const [form] = Form.useForm();

  return (
    <FormDialog
      visible={open}
      title={t('script.create.title', 'New script')}
      width={520}
      okText={t('common.create')}
      cancelText={t('common.cancel')}
      onCancel={() => {
        form.resetFields();
        onClose();
      }}
      onOk={async () => {
        const values = await form.validateFields();
        await createScript(profileId, values.kind, values.name, STARTER_TEMPLATES[values.kind as ScriptKind]);
        form.resetFields();
        onClose();
      }}
    >
      <Form form={form} layout="vertical" initialValues={{ kind: 'main' }}>
        <Form.Item label={t('script.create.kind', 'Kind')} name="kind">
          <Radio.Group>
            <Radio.Button value="main">{t('script.section.main', 'Main')}</Radio.Button>
            <Radio.Button value="library">{t('script.section.library', 'Library')}</Radio.Button>
          </Radio.Group>
        </Form.Item>
        <Form.Item
          label={t('script.create.name', 'Name')}
          name="name"
          rules={[
            { required: true, message: t('script.create.nameRequired', 'Name is required') },
            { pattern: /^[A-Za-z0-9_\-]+$/, message: t('script.create.nameInvalid', 'Letters, numbers, _, - only') },
          ]}
        >
          <CompactInput placeholder="combat-loop" addonAfter=".ts" />
        </Form.Item>
      </Form>
    </FormDialog>
  );
};
