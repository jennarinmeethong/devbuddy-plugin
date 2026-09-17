import { createHash, randomUUID } from "node:crypto";
import { chmodSync, mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { deflateSync } from "node:zlib";

/**
 * A working copy for the analysis screens to read, written as files.
 *
 * DevBuddy reads git as files and never runs it, so the fixture does the same: loose objects,
 * zlib-deflated, with a HEAD and refs written by hand. That keeps the suite free of a `git`
 * dependency in its image and means the repository is exactly what the test says it is.
 *
 * Everything is written world-readable, because the API container reads it as a different user.
 */

export interface CommitSpec {
  message: string;
  files: Record<string, string>;
  tag?: string;
}

export interface WrittenRepository {
  repositoryId: string;
  directory: string;
  commits: string[];
}

export function writeRepository(projectsRoot: string, projectId: string, commits: CommitSpec[]): WrittenRepository {
  const repositoryId = randomUUID();
  const directory = path.join(projectsRoot, projectId, repositoryId);
  const gitDirectory = path.join(directory, ".git");

  makeDirectory(path.join(projectsRoot, projectId));
  makeDirectory(directory);
  makeDirectory(gitDirectory);

  const written: string[] = [];
  let parent: string | null = null;
  let second = 1_758_000_000;

  for (const commit of commits) {
    const tree = writeTree(gitDirectory, commit.files);
    const identity = `E2E Suite <e2e@devbuddy.test> ${second} +0000`;
    const text = [
      `tree ${tree}`,
      ...(parent ? [`parent ${parent}`] : []),
      `author ${identity}`,
      `committer ${identity}`,
      "",
      commit.message,
      "",
    ].join("\n");

    parent = writeObject(gitDirectory, "commit", Buffer.from(text, "utf8"));
    written.push(parent);
    second += 60;

    if (commit.tag) {
      writeText(gitDirectory, `refs/tags/${commit.tag}`, `${parent}\n`);
    }
  }

  if (parent) {
    writeText(gitDirectory, "refs/heads/main", `${parent}\n`);
  }

  writeText(gitDirectory, "HEAD", "ref: refs/heads/main\n");

  // The checked-out files, as a working copy would have them.
  for (const [name, content] of Object.entries(commits.at(-1)?.files ?? {})) {
    writeText(directory, name, content);
  }

  return { repositoryId, directory, commits: written };
}

interface TreeNode {
  files: Map<string, string>;
  directories: Map<string, TreeNode>;
}

function writeTree(gitDirectory: string, files: Record<string, string>): string {
  const root: TreeNode = { files: new Map(), directories: new Map() };

  for (const [name, content] of Object.entries(files)) {
    const parts = name.split("/");
    let node = root;

    for (const part of parts.slice(0, -1)) {
      let child = node.directories.get(part);

      if (!child) {
        child = { files: new Map(), directories: new Map() };
        node.directories.set(part, child);
      }

      node = child;
    }

    node.files.set(parts.at(-1)!, content);
  }

  return writeNode(gitDirectory, root);
}

function writeNode(gitDirectory: string, node: TreeNode): string {
  const entries: { sortKey: string; bytes: Buffer }[] = [];

  for (const [name, content] of node.files) {
    const blob = writeObject(gitDirectory, "blob", Buffer.from(content, "utf8"));
    entries.push({ sortKey: name, bytes: entry("100644", name, blob) });
  }

  for (const [name, child] of node.directories) {
    const tree = writeNode(gitDirectory, child);
    // Git orders a directory as if its name ended in a slash.
    entries.push({ sortKey: `${name}/`, bytes: entry("40000", name, tree) });
  }

  entries.sort((left, right) => Buffer.compare(Buffer.from(left.sortKey), Buffer.from(right.sortKey)));
  return writeObject(gitDirectory, "tree", Buffer.concat(entries.map((item) => item.bytes)));
}

function entry(mode: string, name: string, sha: string): Buffer {
  return Buffer.concat([Buffer.from(`${mode} ${name}\0`, "utf8"), Buffer.from(sha, "hex")]);
}

function writeObject(gitDirectory: string, type: string, content: Buffer): string {
  const raw = Buffer.concat([Buffer.from(`${type} ${content.length}\0`, "utf8"), content]);
  const sha = createHash("sha1").update(raw).digest("hex");
  const directory = path.join(gitDirectory, "objects", sha.slice(0, 2));

  makeDirectory(path.join(gitDirectory, "objects"));
  makeDirectory(directory);
  writeFileSync(path.join(directory, sha.slice(2)), deflateSync(raw), { mode: 0o644 });

  return sha;
}

function writeText(root: string, relative: string, content: string): void {
  const target = path.join(root, ...relative.split("/"));
  let directory = path.dirname(target);
  const pending: string[] = [];

  while (directory.length > root.length) {
    pending.unshift(directory);
    directory = path.dirname(directory);
  }

  pending.forEach(makeDirectory);
  writeFileSync(target, content, { mode: 0o644 });
}

function makeDirectory(directory: string): void {
  mkdirSync(directory, { recursive: true });
  chmodSync(directory, 0o755);
}
