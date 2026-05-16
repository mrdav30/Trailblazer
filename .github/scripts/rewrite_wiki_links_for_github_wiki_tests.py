import importlib.util
import os
import pathlib
import tempfile
import unittest


SCRIPT_PATH = pathlib.Path(__file__).with_name("rewrite-wiki-links-for-github-wiki.py")


def load_rewriter():
    spec = importlib.util.spec_from_file_location("rewrite_wiki_links", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class RewriteWikiLinksForGithubWikiTests(unittest.TestCase):
    def test_rewrites_wiki_page_links_and_repo_escape_links(self):
        rewriter = load_rewriter()

        with tempfile.TemporaryDirectory() as temp_dir:
            root = pathlib.Path(temp_dir)
            wiki_dir = root / "wiki"
            repo_root = root / "repo"
            wiki_dir.mkdir()
            (repo_root / "docs" / "wiki").mkdir(parents=True)
            (repo_root / "src" / "Trailblazer").mkdir(parents=True)
            (repo_root / "README.md").write_text("# Readme\n", encoding="utf-8")

            (wiki_dir / "Home.md").write_text("# Home\n", encoding="utf-8")
            (wiki_dir / "Overview.md").write_text(
                "\n".join(
                    [
                        "[Home](Home.md)",
                        "[Pathing](Pathing.md#requests)",
                        "[Readme](../../README.md)",
                        "[Source](../../src/Trailblazer)",
                    ]
                ),
                encoding="utf-8",
            )
            (wiki_dir / "Pathing.md").write_text("# Pathing\n", encoding="utf-8")

            rewriter.rewrite_wiki_links(
                wiki_dir=wiki_dir,
                repository="mrdav30/Trailblazer",
                sync_sha="abc123",
                source_dir="docs/wiki",
                repo_root=repo_root,
            )

            content = (wiki_dir / "Overview.md").read_text(encoding="utf-8")

        self.assertIn("[Home](Home)", content)
        self.assertIn("[Pathing](Pathing#requests)", content)
        self.assertIn("[Readme](https://github.com/mrdav30/Trailblazer/blob/abc123/README.md)", content)
        self.assertIn("[Source](https://github.com/mrdav30/Trailblazer/tree/abc123/src/Trailblazer)", content)

    def test_ignores_external_images_code_and_non_markdown_local_links(self):
        rewriter = load_rewriter()

        with tempfile.TemporaryDirectory() as temp_dir:
            root = pathlib.Path(temp_dir)
            wiki_dir = root / "wiki"
            repo_root = root / "repo"
            wiki_dir.mkdir()
            (repo_root / "docs" / "wiki").mkdir(parents=True)

            (wiki_dir / "Overview.md").write_text(
                "\n".join(
                    [
                        "[External](https://example.com/Overview.md)",
                        "![Image](Overview.md)",
                        "`[Code](Overview.md)`",
                        "```",
                        "[Fence](Overview.md)",
                        "```",
                        "[Asset](notes.txt)",
                    ]
                ),
                encoding="utf-8",
            )

            rewriter.rewrite_wiki_links(
                wiki_dir=wiki_dir,
                repository="mrdav30/Trailblazer",
                sync_sha="abc123",
                source_dir="docs/wiki",
                repo_root=repo_root,
            )

            content = (wiki_dir / "Overview.md").read_text(encoding="utf-8")

        self.assertIn("[External](https://example.com/Overview.md)", content)
        self.assertIn("![Image](Overview.md)", content)
        self.assertIn("`[Code](Overview.md)`", content)
        self.assertIn("[Fence](Overview.md)", content)
        self.assertIn("[Asset](notes.txt)", content)

    def test_fails_when_wiki_page_link_target_does_not_exist(self):
        rewriter = load_rewriter()

        with tempfile.TemporaryDirectory() as temp_dir:
            root = pathlib.Path(temp_dir)
            wiki_dir = root / "wiki"
            repo_root = root / "repo"
            wiki_dir.mkdir()
            (repo_root / "docs" / "wiki").mkdir(parents=True)
            (wiki_dir / "Overview.md").write_text("[Missing](Missing.md)", encoding="utf-8")

            with self.assertRaises(FileNotFoundError):
                rewriter.rewrite_wiki_links(
                    wiki_dir=wiki_dir,
                    repository="mrdav30/Trailblazer",
                    sync_sha="abc123",
                    source_dir="docs/wiki",
                    repo_root=repo_root,
                )

    def test_fails_when_repo_escape_link_target_does_not_exist(self):
        rewriter = load_rewriter()

        with tempfile.TemporaryDirectory() as temp_dir:
            root = pathlib.Path(temp_dir)
            wiki_dir = root / "wiki"
            repo_root = root / "repo"
            wiki_dir.mkdir()
            (repo_root / "docs" / "wiki").mkdir(parents=True)
            (wiki_dir / "Overview.md").write_text("[Missing](../../MISSING.md)", encoding="utf-8")

            with self.assertRaises(FileNotFoundError):
                rewriter.rewrite_wiki_links(
                    wiki_dir=wiki_dir,
                    repository="mrdav30/Trailblazer",
                    sync_sha="abc123",
                    source_dir="docs/wiki",
                    repo_root=repo_root,
                )


if __name__ == "__main__":
    unittest.main()
