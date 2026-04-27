import React from 'react';
import { Avatar, Button, Dropdown } from 'antd';
import type { MenuProps } from 'antd';
import { CheckOutlined, UserOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useProfileStore } from '../store/profileStore';
import { switchProfile } from '../operations/profileOperations';
import type { Profile } from '../types';

/**
 * Header profile switcher:
 * a flat header button showing the active profile (avatar + name); clicking
 * opens a dropdown of all profiles + a "Manage profiles" entry handled by the
 * parent (which routes to the Profiles tab).
 */
export const ProfileSelector: React.FC<{
  onManageProfiles?: () => void;
}> = ({ onManageProfiles }) => {
  const { t } = useTranslation();
  const profiles = useProfileStore((s) => s.profiles);
  const activeId = useProfileStore((s) => s.activeProfileId);
  const loading = useProfileStore((s) => s.loading);

  const active: Profile | undefined = profiles.find((p) => p.id === activeId);

  const items: MenuProps['items'] = [
    {
      key: 'profiles-header',
      label: `${t('profile.title')} (${profiles.length})`,
      disabled: true,
    },
    { type: 'divider' },
    ...profiles.map((profile) => ({
      key: profile.id,
      label: <ProfileMenuItemLabel profile={profile} active={profile.id === activeId} />,
      onClick: () => {
        if (profile.id !== activeId) void switchProfile(profile.id);
      },
    })),
    ...(onManageProfiles
      ? [
          { type: 'divider' as const },
          {
            key: 'manage',
            label: t('profile.title'),
            onClick: () => onManageProfiles(),
          },
        ]
      : []),
  ];

  return (
    <Dropdown
      menu={{ items }}
      trigger={['click']}
      placement="bottomRight"
      overlayClassName="profile-switcher-menu"
    >
      <Button className="profile-switcher-button" loading={loading}>
        <div className="profile-switcher-content">
          {active ? renderAvatar(active) : <Avatar size={24} icon={<UserOutlined />} />}
          {active ? (
            <span className="profile-switcher-name">{active.name}</span>
          ) : (
            <span className="profile-switcher-placeholder">{t('profile.empty')}</span>
          )}
        </div>
      </Button>
    </Dropdown>
  );
};

const ProfileMenuItemLabel: React.FC<{ profile: Profile; active: boolean }> = ({ profile, active }) => (
  <div className="profile-switcher-menu-item">
    {renderAvatar(profile, 22)}
    <span className="profile-switcher-menu-item__name">{profile.name}</span>
    {active && <CheckOutlined className="profile-switcher-menu-item__check" />}
  </div>
);

function renderAvatar(profile: Profile, size = 24): React.ReactNode {
  return (
    <Avatar
      size={size}
      style={{
        backgroundColor: profile.color || '#1890ff',
        flexShrink: 0,
        fontSize: size <= 22 ? 11 : 12,
        fontWeight: 500,
      }}
    >
      {profile.name.charAt(0).toUpperCase()}
    </Avatar>
  );
}
