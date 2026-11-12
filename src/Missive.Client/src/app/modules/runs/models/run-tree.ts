import { RunPlanFileDto } from '../../../models/messages';
import { PLANS_FOLDER } from '../../../models/workspace';

/** An entry of the Load runs view's tree: a folder, or a run plan file. */
export interface RunTreeNode {
  kind: 'folder' | 'plan';
  /** Workspace-relative path. */
  path: string;
  /** The folder's name, or the plan's name (its file name when it has none). */
  name: string;
  /** Only for plans: the plan file. */
  plan?: RunPlanFileDto;
  /** Only for folders: what is inside, folders first, each sorted by name. */
  children?: RunTreeNode[];
}

/**
 * Builds the tree of the Load runs view from the folders it shows and the plan files, whose paths
 * are inside `root` (the `plans` folder). Folders a plan is in are added even when the list lacks
 * them, so every plan has a place.
 */
export function buildRunTree(
  folders: string[],
  plans: RunPlanFileDto[],
  root = PLANS_FOLDER,
): RunTreeNode[] {
  const top: RunTreeNode[] = [];
  const rootFolder = root;
  const byPath = new Map<string, RunTreeNode>();

  const folderNode = (path: string): RunTreeNode[] => {
    if (!path || path === rootFolder) return top;
    const existing = byPath.get(path);
    if (existing) return existing.children!;
    const slash = path.lastIndexOf('/');
    const node: RunTreeNode = { kind: 'folder', path, name: path.slice(slash + 1), children: [] };
    byPath.set(path, node);
    folderNode(slash < 0 ? '' : path.slice(0, slash)).push(node);
    return node.children!;
  };

  for (const folder of folders) folderNode(folder);
  for (const plan of plans) {
    const slash = plan.path.lastIndexOf('/');
    folderNode(slash < 0 ? '' : plan.path.slice(0, slash)).push({
      kind: 'plan',
      path: plan.path,
      name: plan.name ?? plan.path.slice(slash + 1),
      plan,
    });
  }

  const sort = (nodes: RunTreeNode[]): RunTreeNode[] => {
    nodes.sort(
      (a, b) =>
        (a.kind === b.kind ? 0 : a.kind === 'folder' ? -1 : 1) ||
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }) ||
        a.path.localeCompare(b.path),
    );
    for (const node of nodes) if (node.children) sort(node.children);
    return nodes;
  };
  return sort(top);
}
