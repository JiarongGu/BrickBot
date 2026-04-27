import React, { useState } from 'react';
import { Col, Row } from 'antd';
import { CameraOutlined, ThunderboltOutlined, ToolOutlined, UserOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { CompactCard, CompactSpace } from '@/shared/components/compact';
import { SlideInScreen } from '@/shared/components/common';
import { ProfilePanel } from '@/modules/profile';
import { ActionsPanel } from '@/modules/runner/components/ActionsPanel';
import { CapturePanel } from '@/modules/template';
import './ToolsView.css';

interface ToolCardData {
  key: string;
  title: string;
  description: string;
  icon: React.ReactNode;
  /** Render the actual tool body when the user clicks the card. Lazy because some panels
   *  are heavy (Capture loads window list, Profiles binds the store). */
  render: () => React.ReactNode;
  /** Optional max-width cap for the SlideInScreen panel. Most tools want full remaining width. */
  width?: string;
  /** Optional bodyClassName when the tool manages its own padding. */
  bodyClassName?: string;
}

/**
 * Tools tab — grid of clickable utility cards, each opens in a SlideInScreen.
 *  Add a new entry to `tools` to register a tool.
 */
export const ToolsView: React.FC = () => {
  const { t } = useTranslation();
  const [activeKey, setActiveKey] = useState<string | undefined>();

  const tools: ToolCardData[] = [
    {
      key: 'profiles',
      title: t('tools.profiles.title', 'Profiles'),
      description: t('tools.profiles.description', 'Create, edit, switch, and delete game-target profiles.'),
      icon: <UserOutlined />,
      render: () => <ProfilePanel />,
    },
    {
      key: 'captures',
      title: t('tools.captures.title', 'Captures'),
      description: t('tools.captures.description', 'Grab a frame from the target window, crop a region, save as a template PNG.'),
      icon: <CameraOutlined />,
      render: () => <CapturePanel />,
      bodyClassName: 'tools-view-tool-body',
    },
    {
      key: 'actions',
      title: t('tools.actions.title', 'Actions'),
      description: t('tools.actions.description', 'Fire any brickbot.action(...) registered by the running script.'),
      icon: <ThunderboltOutlined />,
      render: () => <ActionsPanel />,
    },
  ];

  const activeTool = tools.find((tool) => tool.key === activeKey);

  return (
    <>
      <div className="tools-view-container">
        <div className="tools-view-content">
          <div className="tools-view-box">
            <div className="tools-view-header">
              <CompactSpace>
                <ToolOutlined style={{ fontSize: 18 }} />
                <span className="tools-view-title">{t('tools.title', 'Tools')}</span>
              </CompactSpace>
            </div>
            <Row gutter={[16, 16]}>
              {tools.map((tool) => (
                <Col xs={24} sm={12} md={12} lg={8} xl={6} key={tool.key}>
                  <CompactCard
                    hoverable
                    className="tool-card-clickable"
                    bodyStyle={{ padding: 12 }}
                    onClick={() => setActiveKey(tool.key)}
                  >
                    <div className="tool-card-content">
                      <div className="tool-card-header">
                        <span className="tool-card-icon">{tool.icon}</span>
                        <span className="tool-card-title">{tool.title}</span>
                      </div>
                      <div className="tool-card-description">{tool.description}</div>
                    </div>
                  </CompactCard>
                </Col>
              ))}
            </Row>
          </div>
        </div>
      </div>

      <SlideInScreen
        open={!!activeTool}
        title={activeTool?.title ?? ''}
        width={activeTool?.width}
        bodyClassName={activeTool?.bodyClassName}
        onClose={() => setActiveKey(undefined)}
      >
        {activeTool?.render()}
      </SlideInScreen>
    </>
  );
};
