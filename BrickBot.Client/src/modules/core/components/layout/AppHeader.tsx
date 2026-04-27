import React, { useMemo } from 'react';
import { Layout } from 'antd';
import {
  AimOutlined,
  CodeOutlined,
  PlayCircleOutlined,
  SettingOutlined,
  ToolOutlined,
} from '@ant-design/icons';
import classNames from 'classnames';
import { useTranslation } from 'react-i18next';
import { CompactButton } from '@/shared/components/compact';
import { ProfileSelector } from '@/modules/profile';
import './AppHeader.css';

const { Header } = Layout;

export type AppTab = 'runner' | 'scripts' | 'detections' | 'tools' | 'settings';

interface TabItem {
  key: AppTab;
  icon: React.ReactNode;
  label: string;
}

interface AppHeaderProps {
  selectedTab: AppTab;
  onTabChange: (tab: AppTab) => void;
  /** "Manage profiles" action from the ProfileSelector dropdown — typically routes to the Tools tab. */
  onManageProfiles?: () => void;
}

/**
 * AppHeader — top navigation strip:
 * 40px tall, flat text-button tabs flush-left, profile switcher pinned right.
 * Intentionally no app title — branding lives in the footer.
 */
export const AppHeader: React.FC<AppHeaderProps> = ({ selectedTab, onTabChange, onManageProfiles }) => {
  const { t } = useTranslation();

  const tabs: TabItem[] = useMemo(
    () => [
      { key: 'runner', icon: <PlayCircleOutlined />, label: t('app.menu.runner') },
      { key: 'scripts', icon: <CodeOutlined />, label: t('app.menu.scripts', 'Scripts') },
      { key: 'detections', icon: <AimOutlined />, label: t('app.menu.detections', 'Detections') },
      { key: 'tools', icon: <ToolOutlined />, label: t('app.menu.tools', 'Tools') },
      { key: 'settings', icon: <SettingOutlined />, label: t('app.menu.settings') },
    ],
    [t],
  );

  return (
    <Header className="app-header">
      <div className="app-header-tabs">
        {tabs.map((item) => {
          const isSelected = item.key === selectedTab;
          return (
            <CompactButton
              key={item.key}
              type="text"
              icon={item.icon}
              onClick={() => onTabChange(item.key)}
              className={classNames('app-header-tab', { 'app-header-tab-selected': isSelected })}
            >
              {item.label}
            </CompactButton>
          );
        })}
      </div>
      <ProfileSelector onManageProfiles={onManageProfiles} />
    </Header>
  );
};
