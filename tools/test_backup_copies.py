from datetime import datetime, timezone
from pathlib import Path
import tempfile
import unittest

from backup_copies import copy_archive, inspect_archive, retention_plan


class BackupCopiesTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.store = self.root / "store"
        self.store.mkdir()

    def archive(self, day=1):
        path = self.root / f"202601{day:02d}T120000Z-{'a' * 32}.lhbk"
        # Envelope fixture only; authenticity is tested by the real restore tool.
        path.write_bytes(b"LHBK0001" + b"x" * 100)
        return path

    def test_copy_is_verified_and_retries_do_not_overwrite(self):
        source = self.archive()
        first = copy_archive(source, self.store)
        second = copy_archive(source, self.store)
        self.assertTrue(first["copied"])
        self.assertFalse(second["copied"])
        self.assertEqual(source.read_bytes(), Path(first["destination"]).read_bytes())

    def test_private_key_cannot_be_copied(self):
        key = self.root / "aes256.key"
        key.write_bytes(b"secret")
        with self.assertRaises(ValueError):
            copy_archive(key, self.store)
        self.assertEqual([], list(self.store.iterdir()))

    def test_plaintext_disguised_as_archive_rejected(self):
        path = self.archive()
        path.write_bytes(b"plaintext" * 20)
        with self.assertRaises(ValueError):
            inspect_archive(path)

    def test_corrupt_existing_copy_preserved_and_rejected(self):
        source = self.archive()
        target = Path(copy_archive(source, self.store)["destination"])
        target.write_bytes(b"LHBK0001" + b"corrupted" * 20)
        with self.assertRaises(ValueError):
            copy_archive(source, self.store)
        self.assertIn(b"corrupted", target.read_bytes())

    def test_missing_destination_not_created_implicitly(self):
        missing = self.root / "missing"
        with self.assertRaises(ValueError):
            copy_archive(self.archive(), missing)
        self.assertFalse(missing.exists())

    def test_incomplete_copy_without_manifest_is_not_reported_successful(self):
        source = self.archive()
        target = Path(copy_archive(source, self.store)["destination"])
        (target.parent / 'manifest.json').unlink()
        with self.assertRaises(ValueError):
            copy_archive(source, self.store)
        self.assertTrue(target.exists())

    def test_retention_preserves_count_and_minimum_age_and_never_deletes(self):
        for day in range(1, 5):
            copy_archive(self.archive(day), self.store)
        result = retention_plan(self.store, 2, 30, datetime(2026, 1, 31, 12, tzinfo=timezone.utc))
        self.assertEqual(0, result["deletions"])
        self.assertEqual(3, sum(item["retain"] for item in result["archives"]))
        self.assertEqual(4, len(list(self.store.glob("*/*.lhbk"))))

    def test_retention_cannot_keep_less_than_two(self):
        with self.assertRaises(ValueError):
            retention_plan(self.store, 1)

    def test_relative_destination_rejected(self):
        with self.assertRaises(ValueError):
            copy_archive(self.archive(), Path("relative"))


if __name__ == "__main__":
    unittest.main()
