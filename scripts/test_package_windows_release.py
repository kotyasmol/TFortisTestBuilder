"""Release profile selection can be checked without publishing or hardware."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import package_windows_release as release


class ReleaseProfilesTests(unittest.TestCase):
    def test_failed_integrity_check_prevents_publish(self):
        with patch.object(release.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "dotnet test")) as run:
            with self.assertRaises(subprocess.CalledProcessError):
                release.publish_checked_bundle(Path("unused-output"), "1.2.3")
        self.assertEqual(1, run.call_count)
        self.assertEqual(["dotnet", "test"], run.call_args.args[0][:2])
        self.assertTrue(run.call_args.kwargs["check"])

    def test_publish_follows_fresh_integrity_check(self):
        with patch.object(release.subprocess, "run") as run:
            release.publish_checked_bundle(Path("unused-output"), "1.2.3")
        self.assertEqual(["test", "publish"], [c.args[0][1] for c in run.call_args_list])
        self.assertNotIn("--no-build", run.call_args_list[0].args[0])
        self.assertIn(release.PROFILE_UI_TEST_FILTER, run.call_args_list[0].args[0])
        self.assertTrue(all(c.kwargs["check"] for c in run.call_args_list))

    def test_selection_is_explicit_and_preserves_requested_order(self):
        names = ["PSW_UPS_Box_8x2Pro_parallel_start.json", "PSW_2G6F_plus_full_algorithm.json",
                 "RTL_SW_board.json", "RTL_POE_board.json"]
        self.assertEqual(names, [p.name for p in release.select_profiles(names)])
        self.assertEqual(6, len(release.select_profiles()))

    def test_unknown_traversal_empty_and_duplicate_selections_are_rejected(self):
        for names in [[], ["missing.json"], ["../graph.json"], ["RTL_SW_board.json"] * 2]:
            with self.subTest(names=names), self.assertRaises(ValueError):
                release.select_profiles(names)

    def test_staging_only_removes_unselected_profiles_from_bundle(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "source"
            bundle = Path(directory) / "bundle"
            (root / "profiles").mkdir(parents=True)
            (bundle / "profiles/catalog").mkdir(parents=True)
            for name in ["keep.json", "exclude.json"]:
                (root / "profiles" / name).write_text(json.dumps({"name": name}))
                (bundle / "profiles" / name).write_text("old")
            catalog = bundle / "profiles/catalog/equipment.json"
            catalog.write_text("catalog")
            with patch.object(release, "ROOT", root):
                selected = release.select_profiles(["keep.json"])
                release.stage_profiles(bundle, selected)
            self.assertEqual(["keep.json"], [p.name for p in (bundle / "profiles").glob("*.json")])
            self.assertEqual((root / "profiles/keep.json").read_bytes(), (bundle / "profiles/keep.json").read_bytes())
            self.assertTrue((root / "profiles/exclude.json").exists())
            self.assertEqual("catalog", catalog.read_text())


if __name__ == "__main__":
    unittest.main()
