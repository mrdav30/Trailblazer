#!/usr/bin/env python3
"""Rewrite repo-valid wiki source links into GitHub wiki links."""

import argparse
import posixpath
import sys
from pathlib import Path
from urllib.parse import urlparse


def rewrite_wiki_links(wiki_dir, repository, sync_sha, source_dir="docs/wiki", repo_root="."):
    wiki_dir = Path(wiki_dir)
    repo_root = Path(repo_root)
    source_dir = normalize_posix_path(source_dir)

    if not wiki_dir.is_dir():
        raise NotADirectoryError(wiki_dir)

    page_routes = get_page_routes(wiki_dir)
    for markdown_file in sorted(wiki_dir.rglob("*.md")):
        relative_file = markdown_file.relative_to(wiki_dir).as_posix()
        original = markdown_file.read_text(encoding="utf-8")
        rewritten = rewrite_markdown(
            original,
            relative_file,
            page_routes,
            repository,
            sync_sha,
            source_dir,
            repo_root,
        )

        if rewritten != original:
            markdown_file.write_text(rewritten, encoding="utf-8")


def get_page_routes(wiki_dir):
    routes = {}
    for markdown_file in wiki_dir.rglob("*.md"):
        relative_path = markdown_file.relative_to(wiki_dir).as_posix()
        route = relative_path[:-3]
        routes[relative_path] = route

    return routes


def rewrite_markdown(content, relative_file, page_routes, repository, sync_sha, source_dir, repo_root):
    lines = content.splitlines(keepends=True)
    rewritten = []
    in_fence = False
    fence_marker = None

    for line_number, line in enumerate(lines, start=1):
        fence = get_fence_marker(line)
        if fence is not None:
            if in_fence and fence.startswith(fence_marker):
                in_fence = False
                fence_marker = None
            elif not in_fence:
                in_fence = True
                fence_marker = fence

            rewritten.append(line)
            continue

        if in_fence:
            rewritten.append(line)
            continue

        rewritten.append(
            rewrite_line(
                line,
                relative_file,
                page_routes,
                repository,
                sync_sha,
                source_dir,
                repo_root,
                line_number,
            )
        )

    return "".join(rewritten)


def get_fence_marker(line):
    stripped = line.lstrip()
    if stripped.startswith("```"):
        return "```"

    if stripped.startswith("~~~"):
        return "~~~"

    return None


def rewrite_line(line, relative_file, page_routes, repository, sync_sha, source_dir, repo_root, line_number):
    result = []
    index = 0
    in_code = False

    while index < len(line):
        char = line[index]

        if char == "`":
            tick_count = count_repeated(line, index, "`")
            result.append(line[index:index + tick_count])
            index += tick_count
            in_code = not in_code
            continue

        if in_code or char != "[" or is_image_link(line, index):
            result.append(char)
            index += 1
            continue

        parsed = try_parse_link(line, index)
        if parsed is None:
            result.append(char)
            index += 1
            continue

        text, target, end_index = parsed
        rewritten_target = rewrite_target(
            target,
            relative_file,
            page_routes,
            repository,
            sync_sha,
            source_dir,
            repo_root,
            line_number,
        )
        result.append("[")
        result.append(text)
        result.append("](")
        result.append(rewritten_target)
        result.append(")")
        index = end_index

    return "".join(result)


def count_repeated(text, start, char):
    index = start
    while index < len(text) and text[index] == char:
        index += 1

    return index - start


def is_image_link(line, index):
    return index > 0 and line[index - 1] == "!"


def try_parse_link(line, start):
    close_bracket = line.find("]", start + 1)
    if close_bracket < 0 or close_bracket + 1 >= len(line) or line[close_bracket + 1] != "(":
        return None

    close_paren = line.find(")", close_bracket + 2)
    if close_paren < 0:
        return None

    text = line[start + 1:close_bracket]
    target = line[close_bracket + 2:close_paren]
    return text, target, close_paren + 1


def rewrite_target(target, relative_file, page_routes, repository, sync_sha, source_dir, repo_root, line_number):
    if should_ignore_target(target):
        return target

    target_path, fragment = split_fragment(target)
    if not target_path:
        return target

    current_source_dir = posixpath.dirname(posixpath.join(source_dir, relative_file))
    resolved_repo_path = normalize_posix_path(posixpath.join(current_source_dir, target_path))

    if is_within_or_equal(resolved_repo_path, source_dir):
        wiki_relative_path = posixpath.relpath(resolved_repo_path, source_dir)
        if wiki_relative_path in page_routes:
            return page_routes[wiki_relative_path] + fragment

        if target_path.endswith(".md"):
            raise FileNotFoundError(
                f"{relative_file}:{line_number}: wiki page link target does not exist: {target}"
            )

        return target

    repo_path = Path(repo_root, *resolved_repo_path.split("/"))
    if not repo_path.exists():
        raise FileNotFoundError(
            f"{relative_file}:{line_number}: repository link target does not exist: {target}"
        )

    route_kind = "tree" if repo_path.is_dir() else "blob"
    return f"https://github.com/{repository}/{route_kind}/{sync_sha}/{resolved_repo_path}{fragment}"


def should_ignore_target(target):
    if not target or target.startswith("#"):
        return True

    if any(character.isspace() for character in target):
        return True

    parsed = urlparse(target)
    return bool(parsed.scheme or parsed.netloc)


def split_fragment(target):
    if "#" not in target:
        return target, ""

    path, fragment = target.split("#", 1)
    return path, "#" + fragment


def normalize_posix_path(path):
    normalized = posixpath.normpath(str(path).replace("\\", "/"))
    if normalized == ".":
        return ""

    return normalized.lstrip("/")


def is_within_or_equal(path, parent):
    return path == parent or path.startswith(parent.rstrip("/") + "/")


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Rewrite copied docs/wiki Markdown links for GitHub wiki publishing."
    )
    parser.add_argument("wiki_dir", help="Directory containing the copied wiki Markdown files.")
    parser.add_argument("repository", help="GitHub repository in owner/name form.")
    parser.add_argument("sync_sha", help="Commit SHA that the wiki is being synced from.")
    parser.add_argument(
        "--source-dir",
        default="docs/wiki",
        help="Source directory for wiki files in the main repository.",
    )
    parser.add_argument(
        "--repo-root",
        default=".",
        help="Main repository root used to validate and classify escaped links.",
    )
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(sys.argv[1:] if argv is None else argv)
    rewrite_wiki_links(
        wiki_dir=args.wiki_dir,
        repository=args.repository,
        sync_sha=args.sync_sha,
        source_dir=args.source_dir,
        repo_root=args.repo_root,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
