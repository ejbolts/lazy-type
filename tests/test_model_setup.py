import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('setup_models', Path(__file__).parents[1] / 'scripts/setup_models.py')
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)

class ModelSetupTests(unittest.TestCase):
    def test_default_assets_support_cleanup_and_gemma_reword(self):
        self.assertEqual(setup.assets_for(), setup.ASSETS + [setup.TEXT_ASSETS['qwen35'], setup.TEXT_ASSETS['gemma']])
        self.assertFalse(any('qwen3-4b' in asset[0] for asset in setup.assets_for()))

    def test_selection_downloads_only_requested_optional_weights(self):
        for selection, extras in [('qwen35', ['qwen35']), ('gemma', ['gemma']), ('all', ['qwen35', 'gemma'])]:
            with self.subTest(selection=selection):
                assets = setup.assets_for(selection)
                self.assertEqual(assets[len(setup.ASSETS):], [setup.TEXT_ASSETS[e] for e in extras])
                for _, url, sha, _ in assets:
                    self.assertEqual(len(sha), 64)
                    self.assertNotIn('/main/', url)

    def test_benchmark_download_is_verified_and_reused_without_copy(self):
        for asset in setup.TEXT_ASSETS.values():
            with tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                existing = root / 'benchmarks/2026-10-05/models' / asset[1].rsplit('/', 1)[1]
                existing.parent.mkdir(parents=True)
                existing.write_bytes(b'fixture')
                with patch.object(setup, 'ROOT', root), patch.object(setup, 'digest', return_value=asset[2]), patch.object(setup.subprocess, 'run') as download:
                    result = setup.download(asset)
                self.assertEqual(result[0], existing.relative_to(root).as_posix())
                download.assert_not_called()
                self.assertFalse((root / asset[0]).exists())

    def test_bad_benchmark_checksum_cannot_bypass_verified_download(self):
        asset = setup.TEXT_ASSETS['gemma']
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            existing = root / 'benchmarks/test/models' / asset[1].rsplit('/', 1)[1]
            existing.parent.mkdir(parents=True)
            existing.write_bytes(b'wrong')
            with patch.object(setup, 'ROOT', root), patch.object(setup, 'digest', return_value='0' * 64), patch.object(setup.subprocess, 'run') as download:
                with self.assertRaisesRegex(RuntimeError, 'SHA-256 mismatch'):
                    setup.download(asset)
            download.assert_called_once()
            self.assertFalse((root / asset[0]).exists())

if __name__ == '__main__':
    unittest.main()
