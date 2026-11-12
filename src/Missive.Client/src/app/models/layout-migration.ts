import { LayoutMigrationDto } from './messages';
import { PLANS_FOLDER, REQUESTS_FOLDER } from './workspace';

/** How many files a message lists before summing up the rest. */
const MAX_LISTED = 5;

/** The message telling the user what moving their workspace to the current layout did. */
export function describeMigration(migration: LayoutMigrationDto): string {
  const moved: string[] = [];
  if (migration.requests)
    moved.push(`${count(migration.requests, 'request file')} into ${REQUESTS_FOLDER}/`);
  if (migration.plans) moved.push(`${count(migration.plans, 'run plan')} into ${PLANS_FOLDER}/`);

  const lines = [
    moved.length
      ? `Missive now keeps requests in ${REQUESTS_FOLDER}/ and run plans in ${PLANS_FOLDER}/, so it moved ${moved.join(' and ')}. ` +
        'Their history and local-only settings moved with them, and run plans now refer to the new request paths.'
      : `Missive now keeps requests in ${REQUESTS_FOLDER}/ and run plans in ${PLANS_FOLDER}/.`,
  ];
  if (migration.leftInPlace.length) {
    const listed = migration.leftInPlace.slice(0, MAX_LISTED).map((file) => `• ${file}`);
    const more = migration.leftInPlace.length - listed.length;
    lines.push(
      'These files were left where they are:\n' +
        listed.join('\n') +
        (more > 0 ? `\n• and ${more} more` : ''),
    );
  }
  if (migration.error) lines.push(`Not everything could be updated: ${migration.error}`);
  lines.push('Nothing was committed: review the changes in Source control and commit them.');
  return lines.join('\n\n');
}

function count(n: number, noun: string): string {
  return `${n} ${noun}${n === 1 ? '' : 's'}`;
}
