/** A color an environment can be marked with (its `color` in the environment file). */
export type EnvironmentColor =
  'red' | 'orange' | 'yellow' | 'green' | 'teal' | 'blue' | 'purple' | 'pink' | 'gray';

/** How a color is shown: Tailwind classes written out in full so Tailwind generates them. */
export interface EnvironmentColorStyle {
  label: string;
  /** Background, for dots and stripes. */
  bg: string;
  /** Text, for icons. */
  text: string;
  /** Border, for the header's environment picker. */
  border: string;
}

/** Every color, in the order the editor offers them (the same as the host's `EnvironmentColors`). */
export const ENVIRONMENT_COLORS: Record<EnvironmentColor, EnvironmentColorStyle> = {
  red: { label: 'Red', bg: 'bg-red-500', text: 'text-red-500', border: 'border-red-500' },
  orange: {
    label: 'Orange',
    bg: 'bg-orange-500',
    text: 'text-orange-500',
    border: 'border-orange-500',
  },
  yellow: {
    label: 'Yellow',
    bg: 'bg-yellow-400',
    text: 'text-yellow-400',
    border: 'border-yellow-400',
  },
  green: { label: 'Green', bg: 'bg-green-500', text: 'text-green-500', border: 'border-green-500' },
  teal: { label: 'Teal', bg: 'bg-teal-500', text: 'text-teal-500', border: 'border-teal-500' },
  blue: { label: 'Blue', bg: 'bg-blue-500', text: 'text-blue-500', border: 'border-blue-500' },
  purple: {
    label: 'Purple',
    bg: 'bg-purple-500',
    text: 'text-purple-500',
    border: 'border-purple-500',
  },
  pink: { label: 'Pink', bg: 'bg-pink-500', text: 'text-pink-500', border: 'border-pink-500' },
  gray: { label: 'Gray', bg: 'bg-gray-400', text: 'text-gray-400', border: 'border-gray-400' },
};

export const ENVIRONMENT_COLOR_NAMES = Object.keys(ENVIRONMENT_COLORS) as EnvironmentColor[];

/** The style of an environment's color, or `null` when it has none (or one this app doesn't know). */
export function environmentColor(color: string | null | undefined): EnvironmentColorStyle | null {
  return color && Object.hasOwn(ENVIRONMENT_COLORS, color)
    ? ENVIRONMENT_COLORS[color as EnvironmentColor]
    : null;
}
