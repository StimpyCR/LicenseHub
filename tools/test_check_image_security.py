from datetime import datetime, timedelta, timezone
import unittest

from check_image_security import SCANNER, ScanRejected, validate_scan


class ImageSecurityGateTests(unittest.TestCase):
    def setUp(self):
        self.now = datetime(2026, 10, 15, 12, tzinfo=timezone.utc)
        self.identity = "sha256:" + "a" * 64
        self.report = {
            "CreatedAt": self.now.isoformat(),
            "Metadata": {"ImageID": self.identity, "OS": {"Family": "alpine"}},
            "Results": [{"Class": "os-pkgs", "Packages": [{"Name": "synthetic-package"}]}],
        }
        self.database = {"UpdatedAt": (self.now - timedelta(hours=1)).isoformat()}
        self.report_hash = "c" * 64
        self.evidence = {"schemaVersion": 1, "imageId": self.identity, "reportSha256": self.report_hash,
                         "databaseUpdatedAt": self.database["UpdatedAt"], "scannedAt": self.now.isoformat(),
                         "network": "none", "scanner": SCANNER}

    def check(self):
        return validate_scan(self.report, self.database, self.identity, self.now, self.evidence, self.report_hash)

    def test_complete_clean_report_is_accepted(self):
        self.assertEqual(0, self.check()["CRITICAL"])

    def test_report_for_old_image_is_rejected(self):
        self.evidence["imageId"] = "sha256:" + "b" * 64
        with self.assertRaises(ScanRejected):
            self.check()

    def test_missing_inventory_never_becomes_zero_findings(self):
        for results in (None, [], [{"Class": "os-pkgs", "Packages": []}]):
            with self.subTest(results=results), self.assertRaises(ScanRejected):
                self.report["Results"] = results
                self.check()

    def test_critical_high_and_unknown_findings_block(self):
        for severity in ("CRITICAL", "HIGH", "UNKNOWN", None):
            with self.subTest(severity=severity), self.assertRaises(ScanRejected):
                self.report["Results"][0]["Vulnerabilities"] = [{"Severity": severity}]
                self.check()

    def test_medium_findings_are_reported_without_claiming_clean_image(self):
        self.report["Results"][0]["Vulnerabilities"] = [{"Severity": "MEDIUM"}]
        self.assertEqual(1, self.check()["MEDIUM"])

    def test_stale_scan_is_rejected(self):
        self.report["CreatedAt"] = (self.now - timedelta(hours=49)).isoformat()
        with self.assertRaises(ScanRejected):
            self.check()

    def test_stale_database_is_rejected(self):
        self.database["UpdatedAt"] = (self.now - timedelta(hours=49)).isoformat()
        with self.assertRaises(ScanRejected):
            self.check()

    def test_new_advisories_require_new_scan(self):
        self.database["UpdatedAt"] = (self.now + timedelta(minutes=1)).isoformat()
        with self.assertRaises(ScanRejected):
            self.check()

    def test_invalid_or_future_timestamp_is_rejected(self):
        for value in (None, "invalid", "2026-10-15T12:00:00", (self.now + timedelta(hours=1)).isoformat()):
            with self.subTest(value=value), self.assertRaises(ScanRejected):
                self.report["CreatedAt"] = value
                self.check()

    def test_unknown_or_unsupported_os_is_rejected(self):
        for operating_system in ({}, {"Family": "alpine", "EOSL": True}):
            with self.subTest(os=operating_system), self.assertRaises(ScanRejected):
                self.report["Metadata"]["OS"] = operating_system
                self.check()

    def test_altered_report_is_rejected(self):
        self.evidence["reportSha256"] = "d" * 64
        with self.assertRaises(ScanRejected):
            self.check()

    def test_missing_local_provenance_is_rejected(self):
        self.evidence = {}
        with self.assertRaises(ScanRejected):
            self.check()

    def test_manifest_and_config_ids_can_differ_when_export_is_bound(self):
        self.report["Metadata"]["ImageID"] = "sha256:" + "b" * 64
        self.assertEqual(0, self.check()["HIGH"])

    def test_malformed_report_shapes_are_rejected(self):
        original = self.report
        for value in ([], {**original, "Metadata": []}, {**original, "Results": ["invalid"]},
                      {**original, "Results": [{"Class": "os-pkgs", "Packages": [{"Name": "test"}], "Vulnerabilities": "invalid"}]}):
            with self.subTest(value=value), self.assertRaises(ScanRejected):
                self.report = value
                self.check()


if __name__ == "__main__":
    unittest.main()
