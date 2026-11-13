/** What the system file manager is called where the app runs, for the "show folder" action. */
export function revealLabel(userAgent: string): string {
  if (/Windows/i.test(userAgent)) return 'Show in File Explorer';
  if (/Mac/i.test(userAgent)) return 'Show in Finder';
  return 'Show in file manager';
}
