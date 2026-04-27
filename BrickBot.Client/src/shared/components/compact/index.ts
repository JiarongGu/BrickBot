/**
 * Compact component library — minimal-padding, height-consistent wrappers around AntD.
 * Always prefer these over raw AntD components for visual
 * consistency with the rest of the app. See `.claude/rules/ui-design-rules.md`.
 *
 * Usage:
 *   import { CompactCard, CompactButton } from '@/shared/components/compact';
 *   <CompactCard title="Heading"><CompactButton.Primary>Save</CompactButton.Primary></CompactCard>
 */

export {
  CompactButton,
  CompactPrimaryButton,
  CompactTextButton,
  CompactLinkButton,
  CompactDangerButton,
  CompactWarningButton,
  CompactSuccessButton,
} from './CompactButton';
export type { CompactButtonProps, CompactButtonSize } from './CompactButton';

export { CompactCard } from './CompactCard';
export type { CompactCardProps } from './CompactCard';

export { CompactSpace } from './CompactSpace';
export type { CompactSpaceProps } from './CompactSpace';

export { CompactDivider } from './CompactDivider';
export type { CompactDividerProps } from './CompactDivider';

export { CompactTitle, CompactParagraph, CompactText } from './CompactText';
export type { CompactTitleProps, CompactParagraphProps } from './CompactText';

export { CompactAlert } from './CompactAlert';
export type { CompactAlertProps } from './CompactAlert';

export { CompactSection } from './CompactSection';
export type { CompactSectionProps } from './CompactSection';

export { CompactInput, CompactTextArea, CompactPassword } from './CompactInput';
export type { CompactInputProps, CompactTextAreaProps, CompactPasswordProps, CompactInputSize } from './CompactInput';

export { CompactSelect } from './CompactSelect';
export type { CompactSelectProps, CompactSelectSize } from './CompactSelect';

export { CompactSwitch } from './CompactSwitch';
export type { CompactSwitchProps } from './CompactSwitch';
