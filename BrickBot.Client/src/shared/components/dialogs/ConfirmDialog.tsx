import React, { useEffect } from 'react';
import { Modal } from 'antd';
import { CloseOutlined, ExclamationCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { CompactButton, CompactDangerButton, CompactSpace } from '../compact';
import { useDelayedLoading } from '@/shared/hooks/useDelayedLoading';
import './ConfirmDialog.css';

/**
 * ConfirmDialog — opinionated confirmation Modal: warning icon in title, square close
 * button, compact-button footer with optional `okType="danger"`.
 */
export interface ConfirmDialogProps {
  visible: boolean;
  title: React.ReactNode;
  content: React.ReactNode;
  okText?: string;
  cancelText?: string;
  okType?: 'primary' | 'danger' | 'default';
  icon?: React.ReactNode;
  onOk: () => void | Promise<void>;
  onCancel: () => void;
}

export const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
  visible,
  title,
  content,
  okText,
  cancelText,
  okType = 'primary',
  icon = <ExclamationCircleOutlined className="confirm-dialog-icon" />,
  onOk,
  onCancel,
}) => {
  const { t } = useTranslation();
  const resolvedOkText = okText ?? t('common.confirm');
  const resolvedCancelText = cancelText ?? t('common.cancel');
  const { loading, execute, reset } = useDelayedLoading(200);

  useEffect(() => {
    if (!visible) reset();
  }, [visible, reset]);

  const handleOk = async () => {
    try {
      await execute(async () => { await onOk(); });
    } catch (err) {
      if (err instanceof Error && err.message === 'Operation already in progress') return;
      throw err;
    }
  };

  return (
    <Modal
      className="confirm-dialog"
      title={
        <div className="confirm-dialog-title">
          {icon}
          <span>{title}</span>
        </div>
      }
      open={visible}
      onCancel={onCancel}
      centered
      transitionName=""
      maskTransitionName=""
      closeIcon={<div className="confirm-dialog-close-button"><CloseOutlined /></div>}
      footer={
        <CompactSpace className="confirm-dialog-footer">
          <CompactButton onClick={onCancel}>{resolvedCancelText}</CompactButton>
          {okType === 'danger' ? (
            <CompactDangerButton loading={loading} onClick={handleOk}>{resolvedOkText}</CompactDangerButton>
          ) : (
            <CompactButton type={okType === 'primary' ? 'primary' : 'default'} loading={loading} onClick={handleOk}>
              {resolvedOkText}
            </CompactButton>
          )}
        </CompactSpace>
      }
      width={420}
    >
      <div className="confirm-dialog-content">{content}</div>
    </Modal>
  );
};
