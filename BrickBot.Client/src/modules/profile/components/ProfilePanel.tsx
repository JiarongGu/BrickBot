import React, { useState } from 'react';
import {
  ColorPicker,
  Col,
  Flex,
  Form,
  Popconfirm,
  Row,
  Spin,
  Tag,
  Tooltip,
  message,
} from 'antd';
import {
  CheckCircleOutlined,
  ClearOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
  SwapOutlined,
} from '@ant-design/icons';
import classNames from 'classnames';
import { useTranslation } from 'react-i18next';
import {
  CompactButton,
  CompactDangerButton,
  CompactInput,
  CompactPrimaryButton,
  CompactSpace,
  CompactTextArea,
} from '@/shared/components/compact';
import { FormDialog } from '@/shared/components/dialogs';
import { useProfileStore } from '../store/profileStore';
import {
  clearProfileTemp,
  createProfile,
  deleteProfile,
  duplicateProfile,
  switchProfile,
  updateProfile,
} from '../operations/profileOperations';
import type { Profile } from '../types';
import './ProfilePanel.css';

/**
 * Profile management screen and uses the
 * shared compact library + FormDialog for all action surfaces.
 */
export const ProfilePanel: React.FC = () => {
  const { t } = useTranslation();
  const profiles = useProfileStore((s) => s.profiles);
  const activeId = useProfileStore((s) => s.activeProfileId);
  const loading = useProfileStore((s) => s.loading);

  const [createOpen, setCreateOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<Profile | undefined>();
  const [duplicateTarget, setDuplicateTarget] = useState<Profile | undefined>();

  return (
    <>
      <div className="profile-manager-container">
        <Flex vertical gap="large" className="profile-manager-vertical-space">
          <Spin spinning={loading}>
            <Flex vertical gap="middle">
              {profiles.length === 0 && (
                <div className="profile-manager-empty">{t('profile.empty')}</div>
              )}
              {profiles.map((profile) => {
                const isActive = profile.id === activeId;
                const accent = profile.color || '#1890ff';
                return (
                  <Flex
                    key={profile.id}
                    justify="space-between"
                    align="center"
                    className={classNames('profile-manager-item', {
                      'profile-manager-item--active': isActive,
                    })}
                    style={{ borderLeftColor: accent }}
                  >
                    <Flex align="flex-start" gap="middle" className="profile-manager-content">
                      <div className="profile-manager-avatar" style={{ backgroundColor: accent }}>
                        {profile.name.charAt(0).toUpperCase() || '?'}
                      </div>
                      <Flex vertical gap="small" className="profile-manager-content">
                        <CompactSpace wrap>
                          <span className="profile-manager-name">{profile.name}</span>
                          {isActive && (
                            <Tag color="success" icon={<CheckCircleOutlined />}>
                              {t('profile.active')}
                            </Tag>
                          )}
                          {profile.gameName && <Tag color="blue">{profile.gameName}</Tag>}
                        </CompactSpace>
                        {profile.description && (
                          <span className="profile-manager-description">{profile.description}</span>
                        )}
                      </Flex>
                    </Flex>
                    <CompactSpace>
                      {!isActive && (
                        <Tooltip title={t('profile.active')}>
                          <CompactButton
                            icon={<SwapOutlined />}
                            size="small"
                            onClick={() => void switchProfile(profile.id)}
                          />
                        </Tooltip>
                      )}
                      <Tooltip title={t('common.edit')}>
                        <CompactButton
                          icon={<EditOutlined />}
                          size="small"
                          onClick={() => setEditTarget(profile)}
                        />
                      </Tooltip>
                      <Tooltip title={t('common.duplicate')}>
                        <CompactButton
                          icon={<CopyOutlined />}
                          size="small"
                          onClick={() => setDuplicateTarget(profile)}
                        />
                      </Tooltip>
                      <Tooltip title={t('profile.tempFolder.clear')}>
                        <CompactButton
                          icon={<ClearOutlined />}
                          size="small"
                          onClick={async () => {
                            await clearProfileTemp(profile.id);
                            message.success(t('profile.tempFolder.cleared'));
                          }}
                        />
                      </Tooltip>
                      {!isActive && (
                        <Popconfirm
                          title={t('profile.delete.confirmTitle')}
                          description={t('profile.delete.confirmDescription')}
                          okText={t('common.delete')}
                          cancelText={t('common.cancel')}
                          okButtonProps={{ danger: true }}
                          onConfirm={() => void deleteProfile(profile.id)}
                        >
                          <Tooltip title={t('common.delete')}>
                            <CompactDangerButton icon={<DeleteOutlined />} size="small" />
                          </Tooltip>
                        </Popconfirm>
                      )}
                    </CompactSpace>
                  </Flex>
                );
              })}
            </Flex>
          </Spin>

          <CompactPrimaryButton icon={<PlusOutlined />} onClick={() => setCreateOpen(true)} block>
            {t('profile.create.title')}
          </CompactPrimaryButton>
        </Flex>
      </div>

      <CreateProfileDialog open={createOpen} onClose={() => setCreateOpen(false)} />
      {editTarget && (
        <EditProfileDialog target={editTarget} onClose={() => setEditTarget(undefined)} />
      )}
      {duplicateTarget && (
        <DuplicateProfileDialog target={duplicateTarget} onClose={() => setDuplicateTarget(undefined)} />
      )}
    </>
  );
};

const CreateProfileDialog: React.FC<{ open: boolean; onClose: () => void }> = ({ open, onClose }) => {
  const { t } = useTranslation();
  const [form] = Form.useForm();

  return (
    <FormDialog
      visible={open}
      title={t('profile.create.title')}
      width={560}
      okText={t('common.create')}
      cancelText={t('common.cancel')}
      onCancel={() => {
        form.resetFields();
        onClose();
      }}
      onOk={async () => {
        const values = await form.validateFields();
        await createProfile({
          name: values.name,
          description: values.description,
          color: values.color?.toHexString?.() ?? values.color,
          gameName: values.gameName,
        });
        form.resetFields();
        onClose();
      }}
    >
      <Form form={form} layout="vertical" initialValues={{ color: '#1890ff' }}>
        <Row gutter={12}>
          <Col span={16}>
            <Form.Item label={t('profile.field.name')} name="name" rules={[{ required: true }]} style={{ marginBottom: 12 }}>
              <CompactInput placeholder={t('profile.create.namePlaceholder')} />
            </Form.Item>
          </Col>
          <Col span={8}>
            <Form.Item label={t('profile.field.color')} name="color" style={{ marginBottom: 12 }}>
              <ColorPicker showText style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item label={t('profile.field.gameName')} name="gameName" style={{ marginBottom: 12 }}>
          <CompactInput placeholder={t('profile.create.gameNamePlaceholder')} />
        </Form.Item>
        <Form.Item label={t('profile.field.description')} name="description" style={{ marginBottom: 0 }}>
          <CompactTextArea rows={2} placeholder={t('profile.create.descriptionPlaceholder')} />
        </Form.Item>
      </Form>
    </FormDialog>
  );
};

const EditProfileDialog: React.FC<{ target: Profile; onClose: () => void }> = ({ target, onClose }) => {
  const { t } = useTranslation();
  const [form] = Form.useForm();

  return (
    <FormDialog
      visible
      title={t('common.edit')}
      width={560}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      onCancel={onClose}
      onOk={async () => {
        const values = await form.validateFields();
        await updateProfile({
          id: target.id,
          name: values.name,
          description: values.description,
          color: values.color?.toHexString?.() ?? values.color,
          gameName: values.gameName,
        });
        onClose();
      }}
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={{
          name: target.name,
          description: target.description,
          gameName: target.gameName,
          color: target.color || '#1890ff',
        }}
      >
        <Row gutter={12}>
          <Col span={16}>
            <Form.Item label={t('profile.field.name')} name="name" rules={[{ required: true }]} style={{ marginBottom: 12 }}>
              <CompactInput />
            </Form.Item>
          </Col>
          <Col span={8}>
            <Form.Item label={t('profile.field.color')} name="color" style={{ marginBottom: 12 }}>
              <ColorPicker showText style={{ width: '100%' }} />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item label={t('profile.field.gameName')} name="gameName" style={{ marginBottom: 12 }}>
          <CompactInput />
        </Form.Item>
        <Form.Item label={t('profile.field.description')} name="description" style={{ marginBottom: 0 }}>
          <CompactTextArea rows={2} />
        </Form.Item>
      </Form>
    </FormDialog>
  );
};

const DuplicateProfileDialog: React.FC<{ target: Profile; onClose: () => void }> = ({ target, onClose }) => {
  const { t } = useTranslation();
  const [form] = Form.useForm();

  return (
    <FormDialog
      visible
      title={t('profile.duplicate.title')}
      width={420}
      okText={t('common.duplicate')}
      cancelText={t('common.cancel')}
      onCancel={onClose}
      onOk={async () => {
        const values = await form.validateFields();
        await duplicateProfile(target.id, values.newName);
        onClose();
      }}
    >
      <Form form={form} layout="vertical" initialValues={{ newName: `${target.name} (Copy)` }}>
        <Form.Item
          label={t('profile.field.name')}
          name="newName"
          rules={[{ required: true }]}
          style={{ marginBottom: 0 }}
        >
          <CompactInput placeholder={t('profile.duplicate.newNamePlaceholder')} />
        </Form.Item>
      </Form>
    </FormDialog>
  );
};
